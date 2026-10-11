using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaGuard.Openness
{
    public static class ProjectGenerationVerifier
    {
        public static ProjectGenerationVerificationResult Compare(
            ProjectGenerationVerificationExpected expected, ProjectGenerationVerificationObservation actual)
        {
            var r = new ProjectGenerationVerificationResult();
            if (expected == null || actual == null)
            {
                Add(r, "INPUT_MISSING", "project", "Expectation and observation are required.", true);
                return r;
            }
            if (string.IsNullOrWhiteSpace(expected.EvidenceReference))
                Add(r, "EXPECTATION_EVIDENCE_MISSING", "project", "Record the independent expectation source.", true);
            r.Normalization = expected.Normalization;
            if (r.Normalization != "transport-text-v1" && r.Normalization != ProjectGenerationSclProjection.Version)
                Add(r, "NORMALIZATION_UNPROVEN", "project", "Unsupported source comparison profile.", true);
            if (expected.SourceSha256 != null)
                r.ExpectedSourceSha256 = new Dictionary<string, string>(expected.SourceSha256, StringComparer.Ordinal);
            Equal(r, "PROJECT_NAME", "project", expected.ProjectName, actual.ProjectName);
            Equal(r, "PLC_NAME", "plc", expected.PlcName, actual.PlcName);
            Equal(r, "CPU_IDENTITY", "plc", expected.CpuTypeIdentifier, actual.CpuTypeIdentifier);
            if (expected.CpuTypeIdentifier != "OrderNumber:6ES7 212-1AE40-0XB0/V4.7")
                Add(r, "CPU_PROFILE_UNPROVEN", "plc", "Only the exact dispatched 1212C/V4.7 profile is in scope.", true);
            Equal(r, "TIA_VERSION", "project", "V21", actual.TiaVersion);
            Version apiVersion;
            if (!Version.TryParse(actual.ApiVersion, out apiVersion) || apiVersion.Major != 21)
                Add(r, "API_VERSION_UNPROVEN", "project", "A V21 API assembly version is required.", true);
            if (expected.Objects == null || expected.Objects.Count == 0)
                Add(r, "EXPECTATION_EMPTY", "objects", "This verifier requires a nonempty declared program.", true);
            Coverage(r, actual.HardwareComplete, "hardware");
            Coverage(r, actual.ObjectsComplete, "objects");
            Coverage(r, actual.TagTablesComplete, "tag-tables");
            if (actual.ReadFailures == null)
                Add(r, "FAILURE_LEDGER_MISSING", "project", "Read failure ledger is missing.", true);
            else foreach (var f in actual.ReadFailures)
                Add(r, f?.Code ?? "READ_FAILURE", f?.Path ?? "project", f?.Detail ?? "Missing failure entry.", true);

            CompareSets(r, expected.Objects, actual.Objects, o => Key(o.Scope, o.Name), "objects", CompareObject);
            CompareSets(r, expected.TagTables, actual.TagTables, t => Key(t.Scope, t.Name), "tag-tables", CompareTable);
            r.Verdict = r.Findings.Any(f => !f.Unknown) ? "FAIL" : r.Findings.Count == 0 ? "PASS" : "UNKNOWN";
            return r;
        }

        // Only encoding marker and transport line endings change. No whitespace trimming,
        // XML node deletion, token rewriting, comment removal, or assumed SCL/SD conversion.
        public static string NormalizeTransportText(string text)
        {
            if (text == null) return null;
            if (text.StartsWith("\uFEFF", StringComparison.Ordinal)) text = text.Substring(1);
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static void CompareObject(ProjectGenerationVerificationResult r, string path,
            ProjectGenerationVerificationObject e, ProjectGenerationVerificationObject a)
        {
            if (!new[] { "OB", "FB", "FC", "DB", "UDT" }.Contains(e.Kind) ||
                !new[] { "OB", "FB", "FC", "DB", "UDT" }.Contains(a.Kind))
                Add(r, "OBJECT_KIND_UNPROVEN", path, "Object kind is outside the bounded inventory.", true);
            if (!new[] { "scl", "simatic-sd", "simaticml" }.Contains(e.ContentFormat) ||
                !new[] { "scl", "simatic-sd", "simaticml" }.Contains(a.ContentFormat))
                Add(r, "CONTENT_FORMAT_UNPROVEN", path, "Unknown content projection.", true);
            Equal(r, "KIND", path, e.Kind, a.Kind);
            Equal(r, "LANGUAGE", path, e.Language, a.Language);
            if (e.Number.HasValue)
            {
                if (!a.Number.HasValue) Add(r, "NUMBER_UNREADABLE", path, "Block number could not be read.", true);
                else if (e.Number != a.Number) Add(r, "NUMBER_DIFFERENT", path, "Declared block number differs.");
                else r.Compared.Add(path + ":number");
            }
            Coverage(r, e.ContentComplete && a.ContentComplete, path + ":content");
            if (string.IsNullOrWhiteSpace(e.ContentFormat) || string.IsNullOrWhiteSpace(a.ContentFormat) || e.ContentFormat != a.ContentFormat)
                Add(r, "CONTENT_FORMAT_UNPROVEN", path, "No proven projection between the two source formats.", true);
            else
            {
                if (e.Documents == null || a.Documents == null || e.Documents.Count == 0 || a.Documents.Count == 0)
                    Add(r, "CONTENT_MISSING", path, "Nonempty document sets are required.", true);
                else
                {
                    foreach (var key in e.Documents.Keys.Union(a.Documents.Keys).OrderBy(k => k, StringComparer.Ordinal))
                    {
                        if (!e.Documents.ContainsKey(key)) Add(r, "DOCUMENT_UNDECLARED", path + key, "Extra document/resource file.");
                        else if (!a.Documents.ContainsKey(key)) Add(r, "DOCUMENT_MISSING", path + key, "Expected document/resource file missing.");
                        else if (string.IsNullOrWhiteSpace(NormalizeTransportText(e.Documents[key])) || string.IsNullOrWhiteSpace(NormalizeTransportText(a.Documents[key])))
                            Add(r, "DOCUMENT_UNREADABLE", path + key, "Document is missing or empty.", true);
                        else
                        {
                            try
                            {
                                var sourceProjection = r.Normalization == ProjectGenerationSclProjection.Version && e.ContentFormat == "scl";
                                Equal(r, "SOURCE_TEXT", path + key,
                                    sourceProjection ? ProjectGenerationSclProjection.Normalize(e.Documents[key]) : NormalizeTransportText(e.Documents[key]),
                                    sourceProjection ? ProjectGenerationSclProjection.Normalize(a.Documents[key]) : NormalizeTransportText(a.Documents[key]));
                            }
                            catch (Exception ex) { Add(r, "SOURCE_PROJECTION_FAILED", path, ex.GetType().Name + ": " + ex.Message, true); }
                        }
                    }
                }
            }
            Coverage(r, e.InterfaceComplete && a.InterfaceComplete, path + ":interface");
            try
            {
                if (r.Normalization == ProjectGenerationSclProjection.Version)
                {
                    if (e.InterfaceXml == null || a.InterfaceXml == null) Add(r, "INTERFACE_UNREADABLE", path, "Missing interface projection.", true);
                    else if (e.InterfaceXml != ProjectGenerationSclProjection.XmlInterface(a.InterfaceXml)) Add(r, "INTERFACE_DIFFERENT", path, "Source declarations differ from observed XML interface.");
                    else r.Compared.Add(path + ":INTERFACE");
                }
                else Equal(r, "INTERFACE", path, NormalizeTransportText(e.InterfaceXml), NormalizeTransportText(a.InterfaceXml));
            }
            catch (Exception ex) { Add(r, "INTERFACE_PROJECTION_FAILED", path, ex.GetType().Name + ": " + ex.Message, true); }
            Coverage(r, e.InstanceComplete && a.InstanceComplete, path + ":instance");
            if (e.InstanceOfName == null || a.InstanceOfName == null)
                Add(r, "INSTANCE_UNREADABLE", path, "Instance relationship is unknown.", true);
            else if (e.InstanceOfName != a.InstanceOfName)
                Add(r, "INSTANCE_DIFFERENT", path, "Instance target differs.");
            else r.Compared.Add(path + ":instance");
        }

        private static void CompareTable(ProjectGenerationVerificationResult r, string path,
            ProjectGenerationVerificationTagTable e, ProjectGenerationVerificationTagTable a)
        {
            Coverage(r, e.Complete && a.Complete, path + ":tags");
            CompareSets(r, e.Tags, a.Tags, t => Key(t.Name), path, (result, p, et, at) =>
            {
                Equal(result, "TAG_TYPE", p, et.DataType, at.DataType);
                Equal(result, "TAG_ADDRESS", p, et.LogicalAddress, at.LogicalAddress);
            });
        }

        private static void CompareSets<T>(ProjectGenerationVerificationResult r, IList<T> expected, IList<T> actual,
            Func<T, string> key, string path, Action<ProjectGenerationVerificationResult, string, T, T> compare) where T : class
        {
            var e = Index(r, expected, key, path + ":expected");
            var a = Index(r, actual, key, path + ":actual");
            foreach (var k in e.Keys.Union(a.Keys).OrderBy(s => s, StringComparer.Ordinal))
            {
                var p = path + "/" + k;
                if (!e.ContainsKey(k)) Add(r, "OBJECT_UNDECLARED", p, "Generated object was not declared.");
                else if (!a.ContainsKey(k)) Add(r, "OBJECT_MISSING", p, "Declared object missing from observation.");
                else compare(r, p, e[k], a[k]);
            }
        }

        private static Dictionary<string, T> Index<T>(ProjectGenerationVerificationResult r, IList<T> list,
            Func<T, string> key, string path) where T : class
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            if (list == null) { Add(r, "COLLECTION_UNREADABLE", path, "Collection missing.", true); return result; }
            foreach (var value in list)
            {
                string k;
                try { k = value == null ? null : key(value); }
                catch (ArgumentException) { k = null; }
                if (k == null) { Add(r, "IDENTITY_INVALID", path, "Missing scope/name.", true); continue; }
                if (result.ContainsKey(k)) Add(r, "IDENTITY_DUPLICATE", path + "/" + k, "Duplicate object identity.");
                else result.Add(k, value);
            }
            return result;
        }

        private static string Key(params string[] parts)
        {
            if (parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Identity missing.");
            return string.Join("|", parts.Select(p => p.Length + ":" + p));
        }

        private static void Equal(ProjectGenerationVerificationResult r, string code, string path, string e, string a)
        {
            if (string.IsNullOrWhiteSpace(e) || string.IsNullOrWhiteSpace(a)) Add(r, code + "_UNREADABLE", path, "Expected or actual value unavailable.", true);
            else if (!string.Equals(e, a, StringComparison.Ordinal)) Add(r, code + "_DIFFERENT", path, "Expected and actual values differ.");
            else r.Compared.Add(path + ":" + code);
        }

        private static void Coverage(ProjectGenerationVerificationResult r, bool complete, string path)
        {
            if (!complete) Add(r, "COVERAGE_INCOMPLETE", path, "Read/expectation coverage is incomplete.", true);
            else r.Compared.Add(path + ":coverage");
        }

        private static void Add(ProjectGenerationVerificationResult r, string code, string path, string detail, bool unknown = false)
        {
            r.Findings.Add(new ProjectGenerationVerificationFinding { Code = code, Path = path, Detail = detail, Unknown = unknown });
        }
    }
}
