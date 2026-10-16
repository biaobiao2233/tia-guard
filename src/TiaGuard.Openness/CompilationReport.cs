using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Web.Script.Serialization;

namespace TiaGuard.Openness
{
    public sealed class CompilationMessage
    {
        public int Id { get; internal set; }
        public int? ParentId { get; internal set; }
        public string Severity { get; internal set; }
        public string Description { get; internal set; }
        public string EngineeringPath { get; internal set; }
    }

    public sealed class CompilationCollectionFailure
    {
        public string Code { get; internal set; }
        public string Location { get; internal set; }
        // Exception type only: exception messages can contain private machine/project data.
        public string ExceptionType { get; internal set; }
    }

    public sealed class CompilationReport
    {
        internal CompilationReport(string state, int? errors, int? warnings,
            IList<CompilationMessage> messages, IList<CompilationCollectionFailure> failures)
        {
            State = state;
            ErrorCount = errors;
            WarningCount = warnings;
            Messages = new ReadOnlyCollection<CompilationMessage>(messages.ToArray());
            CollectionFailures = new ReadOnlyCollection<CompilationCollectionFailure>(failures.ToArray());
        }

        public string State { get; }
        public int? ErrorCount { get; }
        public int? WarningCount { get; }
        public ReadOnlyCollection<CompilationMessage> Messages { get; }
        public ReadOnlyCollection<CompilationCollectionFailure> CollectionFailures { get; }
        public bool DiagnosticsComplete => CollectionFailures.Count == 0;

        // This is compilation success only, never engineering equivalence or PLC behavior.
        public bool IsSuccessful => DiagnosticsComplete && ErrorCount == 0 &&
            WarningCount.HasValue && (State == "Success" || State == "Warning");

        public void RequireSuccess()
        {
            if (!IsSuccessful)
                throw new InvalidOperationException("Compilation did not produce a complete successful report (state=" +
                    State + ", errors=" + (ErrorCount.HasValue ? ErrorCount.Value.ToString() : "unknown") +
                    ", collectionFailures=" + CollectionFailures.Count + ").");
        }
    }

    public static class CompilationReportJson
    {
        public static string Serialize(CompilationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            // Explicit projection avoids serializer-dependent casing and null omission.
            return new JavaScriptSerializer().Serialize(new
            {
                state = report.State,
                errorCount = report.ErrorCount,
                warningCount = report.WarningCount,
                diagnosticsComplete = report.DiagnosticsComplete,
                messages = report.Messages.Select(m => new
                {
                    id = m.Id, parentId = m.ParentId, severity = m.Severity,
                    description = m.Description, engineeringPath = m.EngineeringPath
                }).ToArray(),
                collectionFailures = report.CollectionFailures.Select(f => new
                {
                    code = f.Code, location = f.Location, exceptionType = f.ExceptionType
                }).ToArray()
            });
        }
    }
}
