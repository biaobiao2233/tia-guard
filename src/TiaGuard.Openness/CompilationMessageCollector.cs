// Adapted from Czarnak/tia-portal-mcp CompileReportProjection.cs / CompileChecker.cs
// at 7301fb809bba338719dd1d3724da055f27945653.
// Copyright (c) 2026 Łukasz Czarnacki. MIT: third-party/compilation-diagnostics/LICENSE.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace TiaGuard.Openness
{
    public sealed class CompilationCollectionOptions
    {
        public int MaxDepth { get; set; } = 16;
        public int MaxMessages { get; set; } = 200;
        public int MaxFieldCharacters { get; set; } = 1024;
        public int MaxTextCharacters { get; set; } = 32000;

        internal CompilationCollectionOptions Snapshot()
        {
            if (MaxDepth < 1 || MaxDepth > 64 || MaxMessages < 1 || MaxMessages > 10000 ||
                MaxFieldCharacters < 1 || MaxFieldCharacters > 32000 ||
                MaxTextCharacters < 1 || MaxTextCharacters > 1000000)
                throw new ArgumentOutOfRangeException(nameof(CompilationCollectionOptions));
            return (CompilationCollectionOptions)MemberwiseClone();
        }
    }

    public static class CompilationMessageCollector
    {
        // Pure projection of an ALREADY returned CompilerResult; never invokes Compile.
        // Reflection keeps both the collector and fault injection independent of Siemens DLLs.
        public static CompilationReport Collect(object compilerResult,
            CompilationCollectionOptions options = null)
        {
            var context = new Context((options ?? new CompilationCollectionOptions()).Snapshot());
            if (compilerResult == null)
            {
                context.Fail("RESULT_UNAVAILABLE", "result");
                return context.Report("Unavailable", null, null);
            }
            var state = context.ReadState(compilerResult, "result.State", false);
            var errors = context.ReadCount(compilerResult, "ErrorCount");
            var warnings = context.ReadCount(compilerResult, "WarningCount");
            context.Visit(context.Read(compilerResult, "Messages", "result.Messages"),
                1, null, "result.Messages");
            context.CheckConsistency(state, errors, warnings);
            return context.Report(state, errors, warnings);
        }

        internal static CompilationReport Unavailable(string code, string location, Exception error = null)
        {
            var context = new Context(new CompilationCollectionOptions());
            context.Fail(code, location, error);
            return context.Report("Unavailable", null, null);
        }

        internal static bool CanRecord(Exception error) => !(error is OutOfMemoryException) &&
            !(error is StackOverflowException) && !(error is AccessViolationException) &&
            !(error is ThreadAbortException);

        private sealed class Context
        {
            private readonly CompilationCollectionOptions limits;
            private readonly List<CompilationMessage> messages = new List<CompilationMessage>();
            private readonly List<CompilationCollectionFailure> failures = new List<CompilationCollectionFailure>();
            private readonly List<object> ancestors = new List<object>();
            private int textCharacters;
            private int visitedMessages;
            private bool sawError;
            private bool sawWarning;
            private bool exhausted;

            internal Context(CompilationCollectionOptions limits) { this.limits = limits; }
            internal CompilationReport Report(string state, int? errors, int? warnings) =>
                new CompilationReport(state, errors, warnings, messages, failures);

            internal void Fail(string code, string location, Exception error = null)
            {
                while (error is TargetInvocationException && error.InnerException != null)
                    error = error.InnerException;
                if (error != null && !CanRecord(error))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                // Keep failure recording bounded even when every property of every row fails.
                if (failures.Count < 63)
                    failures.Add(new CompilationCollectionFailure
                    {
                        Code = code, Location = location, ExceptionType = error?.GetType().Name
                    });
                else if (failures.Count == 63)
                    failures.Add(new CompilationCollectionFailure { Code = "FAILURE_LIMIT", Location = "report" });
            }

            internal object Read(object target, string propertyName, string location)
            {
                try
                {
                    var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                    if (property == null || property.GetIndexParameters().Length != 0)
                    {
                        Fail("PROPERTY_UNAVAILABLE", location);
                        return null;
                    }
                    return property.GetValue(target, null);
                }
                catch (Exception error) when (CanRecord(error))
                {
                    Fail("PROPERTY_READ_FAILED", location, error);
                    return null;
                }
            }

            internal string ReadState(object target, string location, bool message)
            {
                var raw = Read(target, "State", location);
                string state;
                try { state = raw?.ToString(); }
                catch (Exception error) when (CanRecord(error))
                {
                    Fail("STATE_READ_FAILED", location, error);
                    state = null;
                }
                switch (state)
                {
                    case "Error": return message ? "error" : "Error";
                    case "Warning": return message ? "warning" : "Warning";
                    case "Success": return message ? "information" : "Success";
                    case "Information": if (message) return "information"; break;
                }
                Fail("STATE_UNKNOWN", location);
                return message ? "unknown" : "Unknown";
            }

            internal int? ReadCount(object result, string propertyName)
            {
                var value = Read(result, propertyName, "result." + propertyName);
                if (value is int count && count >= 0) return count;
                Fail("COUNT_UNAVAILABLE", "result." + propertyName);
                return null;
            }

            internal void Visit(object value, int depth, int? parentId, string location)
            {
                if (!(value is IEnumerable enumerable) || value is string)
                {
                    Fail("MESSAGES_UNAVAILABLE", location);
                    return;
                }
                // At a boundary inspect at most the next element. An empty collection at
                // exactly MaxMessages/MaxDepth is complete; omitted content is never complete.
                try
                {
                    var index = 0;
                    foreach (var message in enumerable)
                    {
                        if (exhausted) return;
                        if (depth > limits.MaxDepth)
                        {
                            Fail("DEPTH_LIMIT", location);
                            return;
                        }
                        if (visitedMessages >= limits.MaxMessages || textCharacters >= limits.MaxTextCharacters)
                        {
                            Fail("MESSAGE_OR_TEXT_LIMIT", location);
                            exhausted = true;
                            return;
                        }
                        var rowLocation = location + "[" + index++ + "]";
                        visitedMessages++;
                        if (message == null)
                        {
                            Fail("MESSAGE_UNAVAILABLE", rowLocation);
                            continue;
                        }
                        if (ancestors.Exists(a => ReferenceEquals(a, message)))
                        {
                            Fail("MESSAGE_CYCLE", rowLocation);
                            continue;
                        }
                        var severity = ReadState(message, rowLocation + ".State", true);
                        sawError |= severity == "error";
                        sawWarning |= severity == "warning";
                        var row = new CompilationMessage
                        {
                            Id = messages.Count, ParentId = parentId, Severity = severity,
                            Description = ReadText(message, "Description", rowLocation + ".Description", false),
                            EngineeringPath = ReadText(message, "Path", rowLocation + ".Path", true)
                        };
                        messages.Add(row);
                        ancestors.Add(message);
                        try { Visit(Read(message, "Messages", rowLocation + ".Messages"),
                            depth + 1, row.Id, rowLocation + ".Messages"); }
                        finally { ancestors.RemoveAt(ancestors.Count - 1); }
                    }
                }
                catch (Exception error) when (CanRecord(error))
                {
                    // Includes GetEnumerator, MoveNext, Current and Dispose; retain rows.
                    Fail("ENUMERATION_FAILED", location, error);
                }
            }

            private string ReadText(object target, string propertyName, string location, bool nullable)
            {
                var value = Read(target, propertyName, location);
                if (value == null)
                {
                    if (!nullable) Fail("TEXT_UNAVAILABLE", location);
                    return nullable ? null : string.Empty;
                }
                if (!(value is string text))
                {
                    Fail("TEXT_INVALID", location);
                    return nullable ? null : string.Empty;
                }
                var length = Math.Min(text.Length, Math.Min(limits.MaxFieldCharacters,
                    limits.MaxTextCharacters - textCharacters));
                if (length < text.Length)
                {
                    Fail("TEXT_TRUNCATED", location);
                    if (length > 0 && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
                        length--;
                }
                textCharacters += length;
                return length == text.Length ? text : text.Substring(0, length);
            }

            internal void CheckConsistency(string state, int? errors, int? warnings)
            {
                // API totals are authoritative. Parent rows can summarize child rows;
                // neither the number of error rows nor the sum of per-node counts is a total.
                if ((errors == 0 && sawError) || (warnings == 0 && sawWarning) ||
                    (errors > 0 && !sawError) || (warnings > 0 && !sawWarning))
                    Fail("COUNT_MESSAGE_MISMATCH", "result");
                if ((state == "Success" && (errors > 0 || warnings > 0)) ||
                    (state == "Warning" && (errors > 0 || warnings == 0)) ||
                    (state == "Error" && errors == 0))
                    Fail("STATE_COUNT_MISMATCH", "result");
            }
        }
    }
}
