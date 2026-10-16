using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Compilation.Tests
{
    public class CompilationMessageCollectorTests
    {
        public class Result
        {
            public virtual object State { get; set; } = "Success";
            public virtual object ErrorCount { get; set; } = 0;
            public virtual object WarningCount { get; set; } = 0;
            public virtual object Messages { get; set; } = new object[0];
        }

        public class Message
        {
            public virtual object State { get; set; } = "Information";
            public virtual object Description { get; set; } = "Compiling started";
            public virtual object Path { get; set; } = "PLC/Program blocks/Main";
            public virtual object Messages { get; set; } = new object[0];
        }

        public sealed class BrokenMessage : Message
        {
            public override object Description { get => throw new InvalidOperationException("private project path"); set { } }
            public override object State { get => throw new InvalidOperationException(); set { } }
            public override object Path { get => throw new InvalidOperationException(); set { } }
        }

        public sealed class BrokenResult : Result
        {
            public override object State { get => throw new InvalidOperationException(); set { } }
            public override object ErrorCount { get => throw new InvalidOperationException(); set { } }
            public override object WarningCount { get => throw new InvalidOperationException(); set { } }
            public override object Messages { get => throw new InvalidOperationException(); set { } }
        }

        public sealed class BrokenChildren : Message
        {
            public override object Messages { get => throw new InvalidOperationException(); set { } }
        }

        public sealed class FatalMessage : Message
        {
            public override object Description { get => throw new OutOfMemoryException(); set { } }
        }

        private sealed class FaultingEnumerable : IEnumerable
        {
            private readonly string failure;
            internal FaultingEnumerable(string failure) { this.failure = failure; }
            public IEnumerator GetEnumerator()
            {
                if (failure == "GetEnumerator") throw new InvalidOperationException();
                return new FaultingEnumerator(failure);
            }

            private sealed class FaultingEnumerator : IEnumerator, IDisposable
            {
                private readonly string failure;
                private int position;
                internal FaultingEnumerator(string failure) { this.failure = failure; }
                public object Current => failure == "Current" ? throw new InvalidOperationException() : new Message();
                public bool MoveNext() => ++position == 1;
                public void Reset() => throw new NotSupportedException();
                public void Dispose() { if (failure == "Dispose") throw new InvalidOperationException(); }
            }
        }

        [Fact]
        public void ZeroErrorsIsSuccessfulOnlyWithCompleteKnownResult()
        {
            var report = CompilationMessageCollector.Collect(new Result());
            Assert.Equal("Success", report.State);
            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(0, report.WarningCount);
            Assert.True(report.DiagnosticsComplete);
            Assert.True(report.IsSuccessful);
            Assert.Empty(report.Messages);
            report.RequireSuccess();
        }

        [Fact]
        public void WarningsAreSeparateAndCanBeSuccessful()
        {
            var report = CompilationMessageCollector.Collect(new Result
            {
                State = "Warning", WarningCount = 1,
                Messages = new[] { new Message { State = "Warning", Description = "Unused tag" } }
            });
            Assert.True(report.IsSuccessful);
            Assert.Equal("warning", report.Messages.Single().Severity);
            Assert.Equal(1, report.WarningCount);
        }

        [Fact]
        public void MultilevelSummaryRowsNeverIncreaseApiErrorTotal()
        {
            var leaf = new Message { State = "Error", Description = "Undefined symbol" };
            var block = new Message { State = "Error", Description = "Block summary", Messages = new[] { leaf } };
            var root = new Message { State = "Error", Description = "Compiling finished", Messages = new[] { block } };
            var report = CompilationMessageCollector.Collect(new Result
            { State = "Error", ErrorCount = 1, Messages = new[] { root } });
            Assert.Equal(1, report.ErrorCount);
            Assert.Equal(3, report.Messages.Count);
            Assert.Null(report.Messages[0].ParentId);
            Assert.Equal(0, report.Messages[1].ParentId);
            Assert.Equal(1, report.Messages[2].ParentId);
            Assert.Equal("PLC/Program blocks/Main", report.Messages[2].EngineeringPath);
            Assert.True(report.DiagnosticsComplete);
            Assert.False(report.IsSuccessful);
        }

        [Fact]
        public void BrokenRowRetainsChildrenAndFollowingSibling()
        {
            var bad = new BrokenMessage { Messages = new[] { new Message { Description = "child" } } };
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = new object[] { new Message { Description = "before" }, bad, new Message { Description = "after" } } });
            Assert.Equal(new[] { "before", "", "child", "after" }, report.Messages.Select(m => m.Description));
            Assert.Equal("unknown", report.Messages[1].Severity);
            Assert.False(report.DiagnosticsComplete);
            Assert.False(report.IsSuccessful);
            Assert.Contains(report.CollectionFailures, f => f.Location.EndsWith(".Description") && f.ExceptionType == "InvalidOperationException");
            Assert.DoesNotContain("private project path", CompilationReportJson.Serialize(report));
        }

        [Fact]
        public void BrokenChildPropertyRetainsSiblingAndMarksIncomplete()
        {
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = new Message[] { new BrokenChildren(), new Message { Description = "after" } } });
            Assert.Equal(2, report.Messages.Count);
            AssertFailure(report, "PROPERTY_READ_FAILED");
        }

        private static IEnumerable ThrowAfterFirst()
        {
            yield return new Message { Description = "retained" };
            throw new InvalidOperationException("enumerator stopped");
        }

        [Fact]
        public void InterruptedRootEnumerationRetainsPartialRows()
        {
            var report = CompilationMessageCollector.Collect(new Result { Messages = ThrowAfterFirst() });
            Assert.Equal("retained", report.Messages.Single().Description);
            AssertFailure(report, "ENUMERATION_FAILED");
        }

        [Theory]
        [InlineData("GetEnumerator", 0)]
        [InlineData("Current", 0)]
        [InlineData("Dispose", 1)]
        public void EnumeratorAcquisitionCurrentAndDisposalFailuresAreExplicit(string failure, int retained)
        {
            var report = CompilationMessageCollector.Collect(new Result { Messages = new FaultingEnumerable(failure) });
            Assert.Equal(retained, report.Messages.Count);
            AssertFailure(report, "ENUMERATION_FAILED");
        }

        [Fact]
        public void InterruptedChildEnumerationDoesNotLoseOuterSibling()
        {
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = new[] { new Message { Messages = ThrowAfterFirst() }, new Message { Description = "after" } } });
            Assert.Equal(3, report.Messages.Count);
            Assert.Equal("after", report.Messages.Last().Description);
            AssertFailure(report, "ENUMERATION_FAILED");
        }

        [Fact]
        public void NullResultShowsMinimalRegressionForLegacyCoalescing()
        {
            int? unreadErrors = null;
            // Minimal reproduction of the upstream ScaffoldProject decision.
            Assert.True((unreadErrors ?? 0) == 0);
            var report = CompilationMessageCollector.Collect(null);
            Assert.Null(report.ErrorCount);
            Assert.Null(report.WarningCount);
            Assert.Equal("Unavailable", report.State);
            AssertFailure(report, "RESULT_UNAVAILABLE");
            Assert.Throws<InvalidOperationException>(() => report.RequireSuccess());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("InProgress")]
        [InlineData("0")]
        [InlineData("Information")]
        public void UnknownRootStateCannotPass(string state)
        {
            var report = CompilationMessageCollector.Collect(new Result { State = state });
            Assert.Equal("Unknown", report.State);
            AssertFailure(report, "STATE_UNKNOWN");
        }

        [Fact]
        public void UnknownMessageStateIsNotDowngradedToInformation()
        {
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = new[] { new Message { State = "future-state" } } });
            Assert.Equal("unknown", report.Messages.Single().Severity);
            AssertFailure(report, "STATE_UNKNOWN");
        }

        [Theory]
        [InlineData(null)]
        [InlineData(-1)]
        [InlineData("0")]
        [InlineData(0L)]
        public void MissingInvalidOrWronglyTypedCountsCannotPass(object count)
        {
            var report = CompilationMessageCollector.Collect(new Result { ErrorCount = count, WarningCount = count });
            Assert.Null(report.ErrorCount);
            Assert.Null(report.WarningCount);
            AssertFailure(report, "COUNT_UNAVAILABLE");
        }

        [Fact]
        public void UnreadableResultPropertiesCannotPass()
        {
            var report = CompilationMessageCollector.Collect(new BrokenResult());
            Assert.Null(report.ErrorCount);
            Assert.Equal("Unknown", report.State);
            AssertFailure(report, "PROPERTY_READ_FAILED");
        }

        [Fact]
        public void MissingPropertiesAreNotEquivalentToZeroOrEmpty()
        {
            var report = CompilationMessageCollector.Collect(new { State = "Success", ErrorCount = 0 });
            Assert.Null(report.WarningCount);
            AssertFailure(report, "PROPERTY_UNAVAILABLE");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("messages")]
        [InlineData(1)]
        public void InvalidMessageCollectionCannotPass(object messages)
        {
            AssertFailure(CompilationMessageCollector.Collect(new Result { Messages = messages }), "MESSAGES_UNAVAILABLE");
        }

        [Fact]
        public void NullChildCollectionIsExplicitlyIncomplete()
        {
            AssertFailure(CompilationMessageCollector.Collect(new Result
            { Messages = new[] { new Message { Messages = null } } }), "MESSAGES_UNAVAILABLE");
        }

        [Fact]
        public void NullRowCannotSilentlyDisappear()
        {
            AssertFailure(CompilationMessageCollector.Collect(new Result
            { Messages = new object[] { null, new Message() } }), "MESSAGE_UNAVAILABLE");
        }

        [Fact]
        public void MissingPathIsDifferentFromReadableNullPath()
        {
            var noPath = new { State = "Information", Description = "text", Messages = new object[0] };
            AssertFailure(CompilationMessageCollector.Collect(new Result { Messages = new[] { noPath } }), "PROPERTY_UNAVAILABLE");
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = new[] { new Message { Path = null } } });
            Assert.True(report.IsSuccessful);
            Assert.Null(report.Messages[0].EngineeringPath);
        }

        [Theory]
        [InlineData("Success", 0, 0, "Error", "COUNT_MESSAGE_MISMATCH")]
        [InlineData("Success", 0, 0, "Warning", "COUNT_MESSAGE_MISMATCH")]
        [InlineData("Error", 2, 0, "Information", "COUNT_MESSAGE_MISMATCH")]
        [InlineData("Warning", 0, 2, "Information", "COUNT_MESSAGE_MISMATCH")]
        [InlineData("Success", 1, 0, "Error", "STATE_COUNT_MISMATCH")]
        [InlineData("Warning", 1, 1, "Error", "STATE_COUNT_MISMATCH")]
        [InlineData("Warning", 0, 0, "Information", "STATE_COUNT_MISMATCH")]
        [InlineData("Error", 0, 0, "Information", "STATE_COUNT_MISMATCH")]
        public void ContradictoryStateCountsAndDetailsCannotPass(string state, int errors, int warnings,
            string severity, string failure)
        {
            AssertFailure(CompilationMessageCollector.Collect(new Result
            { State = state, ErrorCount = errors, WarningCount = warnings, Messages = new[] { new Message { State = severity } } }), failure);
        }

        [Fact]
        public void NonzeroCountsWithEmptyDetailsAreIncomplete()
        {
            AssertFailure(CompilationMessageCollector.Collect(new Result
            { State = "Error", ErrorCount = 1 }), "COUNT_MESSAGE_MISMATCH");
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(2, false)]
        public void MessageLimitDistinguishesExactCompletionFromOmission(int count, bool complete)
        {
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = Enumerable.Range(0, count).Select(_ => new Message()).ToArray() },
                new CompilationCollectionOptions { MaxMessages = 1 });
            Assert.Single(report.Messages);
            Assert.Equal(complete, report.DiagnosticsComplete);
            Assert.Equal(complete, report.IsSuccessful);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void DepthLimitDistinguishesEmptyChildrenFromOmission(bool child, bool complete)
        {
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = new[] { new Message { Messages = child ? new[] { new Message() } : new Message[0] } } },
                new CompilationCollectionOptions { MaxDepth = 1 });
            Assert.Single(report.Messages);
            Assert.Equal(complete, report.DiagnosticsComplete);
        }

        [Fact]
        public void TruncatedTextCannotPassAndDoesNotSplitSurrogatePairs()
        {
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = new[] { new Message { Description = "a\U0001F680b", Path = null } } },
                new CompilationCollectionOptions { MaxFieldCharacters = 2 });
            Assert.Equal("a", report.Messages[0].Description);
            AssertFailure(report, "TEXT_TRUNCATED");
        }

        [Fact]
        public void AggregateTextLimitIsExplicit()
        {
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = new[] { new Message { Description = "abc", Path = "de" }, new Message() } },
                new CompilationCollectionOptions { MaxTextCharacters = 4 });
            Assert.Single(report.Messages);
            Assert.Equal("d", report.Messages[0].EngineeringPath);
            AssertFailure(report, "TEXT_TRUNCATED");
        }

        [Fact]
        public void CycleIsBoundedAndCannotPass()
        {
            var node = new Message();
            node.Messages = new[] { node };
            var report = CompilationMessageCollector.Collect(new Result { Messages = new[] { node } });
            Assert.Single(report.Messages);
            AssertFailure(report, "MESSAGE_CYCLE");
        }

        private static IEnumerable ForeverNull()
        {
            while (true) yield return null;
        }

        [Fact]
        public void MalformedRowsStillConsumeTraversalBudget()
        {
            var report = CompilationMessageCollector.Collect(new Result { Messages = ForeverNull() },
                new CompilationCollectionOptions { MaxMessages = 3 });
            Assert.Empty(report.Messages);
            AssertFailure(report, "MESSAGE_OR_TEXT_LIMIT");
        }

        [Fact]
        public void FailureListHasBoundAndOmissionMarker()
        {
            var report = CompilationMessageCollector.Collect(new Result
            { Messages = Enumerable.Range(0, 100).Select(_ => new BrokenMessage()).ToArray() });
            Assert.Equal(64, report.CollectionFailures.Count);
            AssertFailure(report, "FAILURE_LIMIT");
        }

        [Fact]
        public void JsonUsesContractNamesAndRetainsUnknownCountsAndNullablePath()
        {
            var unknown = new JavaScriptSerializer().DeserializeObject(
                CompilationReportJson.Serialize(CompilationMessageCollector.Collect(null))) as Dictionary<string, object>;
            Assert.Equal(new[] { "collectionFailures", "diagnosticsComplete", "errorCount", "messages", "state", "warningCount" },
                unknown.Keys.OrderBy(k => k));
            Assert.Null(unknown["errorCount"]);
            Assert.Null(unknown["warningCount"]);
            Assert.Equal(false, unknown["diagnosticsComplete"]);
            var json = CompilationReportJson.Serialize(CompilationMessageCollector.Collect(new Result
            { Messages = new[] { new Message { Path = null } } }));
            Assert.Contains("\"engineeringPath\":null", json);
            Assert.Contains("\"parentId\":null", json);
        }

        [Fact]
        public void ServiceInvocationIsOnceAndCollectionFailureDoesNotRetry()
        {
            var calls = 0;
            var report = CompilationService.Invoke(() => { calls++; return new BrokenResult(); });
            Assert.Equal(1, calls);
            AssertFailure(report, "PROPERTY_READ_FAILED");
        }

        [Fact]
        public void InvocationFailurePreservesUnknownCounts()
        {
            var report = CompilationService.Invoke(() => throw new InvalidOperationException("private path"));
            Assert.Null(report.ErrorCount);
            Assert.Null(report.WarningCount);
            AssertFailure(report, "COMPILE_INVOCATION_FAILED");
        }

        [Fact]
        public void InvalidLimitsAreRejectedBeforeActiveCompile()
        {
            var calls = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => CompilationService.Invoke(
                () => { calls++; return new Result(); }, new CompilationCollectionOptions { MaxDepth = 0 }));
            Assert.Equal(0, calls);
        }

        [Fact]
        public void ActiveOperationCannotChangePreviouslyValidatedLimits()
        {
            var limits = new CompilationCollectionOptions { MaxMessages = 1 };
            var report = CompilationService.Invoke(() =>
            {
                limits.MaxMessages = 100;
                return new Result { Messages = new[] { new Message(), new Message() } };
            }, limits);
            Assert.Single(report.Messages);
            AssertFailure(report, "MESSAGE_OR_TEXT_LIMIT");
        }

        [Fact]
        public void FatalErrorsAreNotHiddenAsDiagnostics()
        {
            Assert.Throws<OutOfMemoryException>(() => CompilationService.Invoke(() => throw new OutOfMemoryException()));
            Assert.Throws<OutOfMemoryException>(() => CompilationMessageCollector.Collect(new Result
            { Messages = new[] { new FatalMessage() } }));
        }

        private static void AssertFailure(CompilationReport report, string code)
        {
            Assert.False(report.DiagnosticsComplete);
            Assert.False(report.IsSuccessful);
            Assert.Contains(report.CollectionFailures, f => f.Code == code);
        }
    }
}
