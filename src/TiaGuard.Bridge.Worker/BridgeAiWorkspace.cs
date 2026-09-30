using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using TiaGuard.Openness;

namespace TiaGuard.Bridge.Worker
{
    internal sealed class BridgeAiWorkspace : IDisposable
    {
        private readonly string _root;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private Cache _cache;
        private int _epoch;
        private bool _disposed;

        public BridgeAiWorkspace()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "tia-guard-bridge",
                Guid.NewGuid().ToString("N"));
        }

        public int Epoch { get { return _epoch; } }

        public string Identity(TiaProjectSession session)
        {
            var project = RequireSession(session);
            var contentId = ReadContentId(session);
            return Envelope(project, contentId, false, "{\"kind\":\"identity\"}");
        }

        public string Query(TiaProjectSession session, string payloadJson)
        {
            var request = _json.Deserialize<AiQueryPayload>(payloadJson ?? "{}") ?? new AiQueryPayload();
            var view = Materialize(session, request.Force);
            string json;
            switch ((request.Kind ?? string.Empty).Trim())
            {
                case "project":
                    json = RoundTripJson.Serialize(AiEngineeringContextQuery.ProjectContext(view));
                    break;
                case "graph":
                    json = RoundTripJson.Serialize(AiEngineeringContextQuery.ProgramGraph(view, request.Block));
                    break;
                case "network":
                    json = RoundTripJson.Serialize(AiEngineeringContextQuery.Network(view, request.Block, request.Network));
                    break;
                case "whereUsed":
                    json = RoundTripJson.Serialize(AiEngineeringContextQuery.WhereUsed(view, request.Symbol));
                    break;
                default:
                    throw new ArgumentException("AI_CONTEXT_REJECTED: QUERY_KIND");
            }
            json = json.Trim();
            AiEngineeringContextQuery.RejectBlobLeakage(json);
            return Envelope(RequireSession(session), _cache.ContentId, _cache.Hit, json);
        }

        public string PreviewPatch(TiaProjectSession session, string patchJson)
        {
            RequireOffline(session);
            Materialize(session, false);
            var canonical = AiEngineeringPatch.Canonicalize(patchJson);
            var preview = Path.Combine(_root, "preview-" + Guid.NewGuid().ToString("N"));
            try
            {
                var plan = AiEngineeringPatch.Plan(_cache.SourceDirectory, canonical, preview);
                return Envelope(RequireSession(session), _cache.ContentId, _cache.Hit,
                    RoundTripJson.Serialize(plan).Trim());
            }
            finally
            {
                AiEngineeringPatch.DeleteOwnedDirectory(preview);
            }
        }

        public string ApplyPatch(TiaProjectSession session, string payloadJson)
        {
            RequireOffline(session);
            var payload = _json.Deserialize<AiApplyPayload>(payloadJson ?? "{}") ?? new AiApplyPayload();
            if (!string.IsNullOrWhiteSpace(payload.InjectFailure) && !TestHooksEnabled())
                throw new InvalidOperationException("AI_PATCH_REJECTED: TEST_HOOK_DISABLED");
            if (!string.IsNullOrWhiteSpace(payload.InjectFailure) &&
                !System.Text.RegularExpressions.Regex.IsMatch(payload.InjectFailure, "^[A-Z0-9_]{1,64}$"))
                throw new InvalidOperationException("AI_PATCH_REJECTED: TEST_HOOK_INVALID");
            if (payload.ExpectedEpoch != _epoch)
                throw new InvalidOperationException("AI_PATCH_REJECTED: STALE_EPOCH");

            var before = Materialize(session, true);
            if (!string.Equals(_cache.ContentId, payload.ExpectedContentId, StringComparison.Ordinal))
                throw new InvalidOperationException("AI_PATCH_REJECTED: STALE_CONTENT");
            var canonical = AiEngineeringPatch.Canonicalize(payload.PatchJson);
            var request = DeserializePatch(canonical);
            var fingerprint = AiEngineeringPatch.Fingerprint(before, request);
            if (!string.Equals(fingerprint, payload.ExpectedFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("AI_PATCH_REJECTED: STALE_FINGERPRINT");

            var preview = Path.Combine(_root, "apply-" + Guid.NewGuid().ToString("N"));
            var rollbackXml = Path.Combine(_root, "rollback-" + Guid.NewGuid().ToString("N") + ".xml");
            AiPatchPlan plan;
            try
            {
                plan = AiEngineeringPatch.Plan(_cache.SourceDirectory, canonical, preview);
                if (request.Operation == "upsert_tag")
                    RequireLiveTagMatchesPreview(session, plan);
                if (request.Operation == "replace_output_condition")
                {
                    var current = RoundTripBuildInput.LoadSource(_cache.SourceDirectory);
                    File.Copy(current.BlockSourcePath, rollbackXml, false);
                }
            }
            catch
            {
                AiEngineeringPatch.DeleteOwnedDirectory(preview);
                if (File.Exists(rollbackXml)) File.Delete(rollbackXml);
                throw;
            }

            _epoch++;
            var previousSource = _cache.SourceDirectory;
            _cache = null;
            var mutated = false;
            try
            {
                if (request.Operation == "upsert_tag")
                {
                    mutated = true;
                    session.UpsertRootTagForBridge(
                        request.Target.Table, request.Target.Tag, request.DataType, request.LogicalAddress);
                }
                else if (request.Operation == "replace_output_condition")
                {
                    mutated = true;
                    session.ImportMainBlockForBridge(plan.ModifiedBlockXmlPath);
                }
                else
                {
                    throw new InvalidOperationException("AI_PATCH_REJECTED: OPERATION_UNSUPPORTED");
                }

                if (!string.IsNullOrWhiteSpace(payload.InjectFailure))
                    return RejectMutation(session, request, rollbackXml, plan, -1, 0,
                        "not_run", "not_run", "fail", payload.InjectFailure);

                var compile = session.CompilePlcForVerification();
                if (compile.Errors != 0)
                    return RejectMutation(session, request, rollbackXml, plan, compile.Errors, compile.Warnings,
                        "not_run", "not_run", "not_run", "compile errors");

                string verified;
                try
                {
                    verified = ExportVerified(session);
                }
                catch (Exception error)
                {
                    return RejectMutation(session, request, rollbackXml, plan, compile.Errors, compile.Warnings,
                        "fail", "not_run", "not_run", "canonical export was not deterministic: " + error.Message);
                }

                IsolatedRoundTripResult roundTrip;
                try
                {
                    var rebuildDirectory = Path.Combine(_root, "rebuild-" + Guid.NewGuid().ToString("N"));
                    roundTrip = ProveRoundTripOutOfProcess(verified, rebuildDirectory);
                }
                catch (Exception error)
                {
                    return RejectMutation(session, request, rollbackXml, plan, compile.Errors, compile.Warnings,
                        "pass", "fail", "not_run", "round-trip verify failed: " + error.Message);
                }
                if (!roundTrip.Built)
                    return RejectMutation(session, request, rollbackXml, plan, compile.Errors, compile.Warnings,
                        "pass", "fail", "not_run", "rebuilt project compile failed: " + roundTrip.Message);
                if (!string.Equals(roundTrip.Verdict, "pass", StringComparison.Ordinal))
                    return RejectMutation(session, request, rollbackXml, plan, compile.Errors, compile.Warnings,
                        "pass", string.IsNullOrWhiteSpace(roundTrip.Verdict) ? "fail" : roundTrip.Verdict, "not_run",
                        "round-trip verify " + roundTrip.Verdict +
                        (string.IsNullOrWhiteSpace(roundTrip.BlockedCode) ? string.Empty : ": " + roundTrip.BlockedCode));

                var after = AiEngineeringRenderer.Read(verified);
                string semanticReason;
                if (!SemanticsMatch(request, plan, before, after, out semanticReason))
                    return RejectMutation(session, request, rollbackXml, plan, compile.Errors, compile.Warnings,
                        "pass", "pass", "fail", semanticReason);

                session.SaveDisposableCopyForBridge();
                if (!string.IsNullOrWhiteSpace(previousSource))
                    AiEngineeringPatch.DeleteOwnedDirectory(previousSource);
                var contentId = ReadContentId(session);
                Remember(RequireSession(session), contentId, verified, after, false);
                return RoundTripJson.Serialize(AiEngineeringPatch.CreateApplyResult(
                    request.Operation, compile.Errors, compile.Warnings, "pass", "pass", "pass", null,
                    false, true, null, contentId, ResultNetwork(request, after), ResultTag(request, after), _epoch)).Trim();
            }
            catch (Exception error)
            {
                if (!mutated)
                    throw;
                return RejectMutation(session, request, rollbackXml, plan, 0, 0,
                    "not_run", "not_run", "not_run", error.Message);
            }
            finally
            {
                AiEngineeringPatch.DeleteOwnedDirectory(preview);
                if (File.Exists(rollbackXml)) File.Delete(rollbackXml);
            }
        }

        public void Invalidate()
        {
            _epoch++;
            _cache = null;
            AiEngineeringPatch.DeleteOwnedDirectory(_root);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cache = null;
            AiEngineeringPatch.DeleteOwnedDirectory(_root);
        }

        private AiEngineeringView Materialize(TiaProjectSession session, bool force)
        {
            var project = RequireSession(session);
            RequireOffline(session);
            var contentId = ReadContentId(session);
            if (!force && _cache != null &&
                _cache.Epoch == _epoch &&
                string.Equals(_cache.Binding, Binding(project), StringComparison.Ordinal) &&
                string.Equals(_cache.ContentId, contentId, StringComparison.Ordinal) &&
                Directory.Exists(_cache.SourceDirectory))
            {
                _cache.Hit = true;
                return _cache.View;
            }

            Directory.CreateDirectory(_root);
            var source = Path.Combine(_root, "src-" + Guid.NewGuid().ToString("N"));
            session.ExportRoundTripSource(source);
            var exportedContentId = ReadContentId(session);
            var view = AiEngineeringRenderer.Read(source);
            if (_cache != null && !string.Equals(_cache.SourceDirectory, source, StringComparison.OrdinalIgnoreCase))
                AiEngineeringPatch.DeleteOwnedDirectory(_cache.SourceDirectory);
            Remember(project, exportedContentId, source, view, false);
            return view;
        }

        private string ExportVerified(TiaProjectSession session)
        {
            var first = Path.Combine(_root, "verify-" + Guid.NewGuid().ToString("N"));
            var second = Path.Combine(_root, "verify-" + Guid.NewGuid().ToString("N"));
            try
            {
                session.ExportRoundTripSource(first);
                session.ExportRoundTripSource(second);
                var compared = RoundTripVerifier.CompareSources(first, second);
                if (!string.Equals(compared.Verdict, "pass", StringComparison.Ordinal))
                    throw new InvalidOperationException("EXPORT_NOT_DETERMINISTIC");
                return first;
            }
            finally
            {
                AiEngineeringPatch.DeleteOwnedDirectory(second);
            }
        }

        private string RejectMutation(
            TiaProjectSession session, AiPatchRequest request, string rollbackXml, AiPatchPlan plan,
            int compileErrors, int compileWarnings, string exportVerdict, string roundTripVerdict,
            string semanticVerdict, string reason)
        {
            var rollbackOk = true;
            string rollbackError = null;
            try
            {
                Rollback(session, request, rollbackXml, plan);
            }
            catch (Exception error)
            {
                rollbackOk = false;
                rollbackError = error.Message;
            }
            return RoundTripJson.Serialize(AiEngineeringPatch.CreateApplyResult(
                request.Operation, compileErrors, compileWarnings, exportVerdict, roundTripVerdict,
                semanticVerdict, reason, true, rollbackOk, rollbackError, null, null, null, _epoch)).Trim();
        }

        private static bool SemanticsMatch(
            AiPatchRequest request, AiPatchPlan plan, AiEngineeringView before, AiEngineeringView after,
            out string reason)
        {
            try
            {
                if (request.Operation == "upsert_tag")
                {
                    AiEngineeringPatch.AssertTagUpdatePreservesLogic(
                        before, after, request.Target.Tag, request.LogicalAddress);
                    var tag = FindTag(after, request.Target.Table, request.Target.Tag);
                    if (tag == null ||
                        !string.Equals(tag.DataType, request.DataType, StringComparison.Ordinal))
                    {
                        reason = "resulting tag does not match the preview";
                        return false;
                    }
                }
                else
                {
                    AiEngineeringPatch.AssertNeighborsUnchanged(before, after, request.Target.Network);
                    var block = AiEngineeringContextQuery.ResolveBlock(after, request.Target.Block);
                    var network = AiEngineeringContextQuery.ResolveNetwork(block, request.Target.Network);
                    var expected = plan.ExpectedNetwork == null || plan.ExpectedNetwork.Analysis == null ||
                        plan.ExpectedNetwork.Analysis.Writes.Count != 1
                        ? null : plan.ExpectedNetwork.Analysis.Writes[0].Expression;
                    var actual = network.Analysis == null || network.Analysis.Status != "supported" ||
                        network.Analysis.Writes.Count != 1
                        ? null : network.Analysis.Writes[0].Expression;
                    if (!AiEngineeringPatch.ExpressionEquals(expected, actual))
                    {
                        reason = "resulting LAD semantics do not match the preview";
                        return false;
                    }
                }
            }
            catch (InvalidOperationException error)
            {
                reason = error.Message;
                return false;
            }
            reason = null;
            return true;
        }

        private static AiNetwork ResultNetwork(AiPatchRequest request, AiEngineeringView after)
        {
            if (request.Operation != "replace_output_condition") return null;
            var block = AiEngineeringContextQuery.ResolveBlock(after, request.Target.Block);
            return AiEngineeringContextQuery.ResolveNetwork(block, request.Target.Network);
        }

        private static AiPatchTagState ResultTag(AiPatchRequest request, AiEngineeringView after)
        {
            if (request.Operation != "upsert_tag") return null;
            var tag = FindTag(after, request.Target.Table, request.Target.Tag);
            return tag == null ? null : new AiPatchTagState
            {
                Table = request.Target.Table,
                Name = tag.Name,
                Exists = true,
                DataType = tag.DataType,
                LogicalAddress = tag.Address,
                SourceRef = tag.SourceRef,
                Comment = tag.Comment
            };
        }

        private static AiTag FindTag(AiEngineeringView view, string tableName, string tagName)
        {
            var table = view.TagTables.Find(item => string.Equals(item.Name, tableName, StringComparison.Ordinal));
            return table == null ? null : table.Tags.Find(item => string.Equals(item.Name, tagName, StringComparison.Ordinal));
        }

        private static void RequireLiveTagMatchesPreview(TiaProjectSession session, AiPatchPlan plan)
        {
            var live = session.ReadRootTagStateForBridge(plan.Target.Table, plan.Target.Tag);
            var expected = plan.CurrentTag ?? new AiPatchTagState();
            if (live.Exists != expected.Exists)
                throw new InvalidOperationException("AI_PATCH_REJECTED: STALE_TAG");
            if (!live.Exists) return;
            if (!string.Equals(live.DataType, expected.DataType, StringComparison.Ordinal) ||
                !SameAddress(live.LogicalAddress, expected.LogicalAddress))
                throw new InvalidOperationException("AI_PATCH_REJECTED: STALE_TAG");
        }

        private static bool SameAddress(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(NormalizeAddress(left), NormalizeAddress(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Trim().TrimStart('%').Replace(" ", string.Empty);
        }

        public static int RunIsolatedRoundTrip(string[] args)
        {
            if (args == null || args.Length != 4)
            {
                Console.Error.WriteLine("Usage: --isolated-roundtrip <source> <output> <result>");
                return 2;
            }
            var result = new IsolatedRoundTripResult();
            try
            {
                var built = RoundTripBuilder.Build(args[1], args[2]);
                result.Built = true;
                result.ProjectFile = built.ProjectFile;
                var verify = RoundTripVerifier.VerifySourceAgainstProject(args[1], built.ProjectFile);
                result.Verdict = verify.Verdict;
                result.BlockedCode = verify.BlockedCode;
                result.Message = verify.Verdict;
            }
            catch (Exception error)
            {
                result.Built = false;
                result.Verdict = "fail";
                result.Message = error.ToString();
            }
            File.WriteAllText(args[3], new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(result),
                new UTF8Encoding(false));
            return 0;
        }

        private static IsolatedRoundTripResult ProveRoundTripOutOfProcess(string sourceRoot, string outputDirectory)
        {
            var exe = Assembly.GetExecutingAssembly().Location;
            var resultPath = outputDirectory + ".json";
            var start = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--isolated-roundtrip " + Quote(sourceRoot) + " " + Quote(outputDirectory) + " " + Quote(resultPath),
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using (var process = Process.Start(start))
            {
                if (process == null)
                    throw new InvalidOperationException("The isolated round-trip process did not start.");
                var stderr = process.StandardError.ReadToEndAsync();
                var stdout = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(22 * 60 * 1000))
                {
                    try { process.Kill(); } catch (Exception) { }
                    throw new InvalidOperationException("The isolated round-trip timed out.");
                }
                process.WaitForExit();
                var details = (stderr.GetAwaiter().GetResult() + stdout.GetAwaiter().GetResult()).Trim();
                if (!File.Exists(resultPath))
                    throw new InvalidOperationException(
                        "The isolated round-trip produced no result. " + details);
                var result = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                    .Deserialize<IsolatedRoundTripResult>(File.ReadAllText(resultPath));
                if (result == null)
                    throw new InvalidOperationException("The isolated round-trip result was empty. " + details);
                if (string.IsNullOrWhiteSpace(result.Message)) result.Message = details;
                return result;
            }
        }

        private static bool TestHooksEnabled()
        {
            return string.Equals(
                Environment.GetEnvironmentVariable("TIA_GUARD_BRIDGE_TEST_HOOKS"),
                "1", StringComparison.Ordinal);
        }

        private static void Rollback(TiaProjectSession session, AiPatchRequest request, string previousXml, AiPatchPlan plan)
        {
            try
            {
                if (request.Operation == "replace_output_condition")
                {
                    if (string.IsNullOrWhiteSpace(previousXml) || !File.Exists(previousXml))
                        throw new InvalidOperationException("the pre-apply LAD block is missing");
                    session.ImportMainBlockForBridge(previousXml);
                }
                else if (request.Operation == "upsert_tag")
                {
                    var prior = plan.CurrentTag ?? new AiPatchTagState();
                    session.RestoreRootTagForBridge(
                        request.Target.Table, request.Target.Tag, prior.Exists, prior.DataType, prior.LogicalAddress);
                }
                var compile = session.CompilePlcForVerification();
                if (compile.Errors != 0)
                    throw new InvalidOperationException("rollback compile reported " + compile.Errors + " errors");
                session.SaveDisposableCopyForBridge();
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("AI_PATCH_REJECTED: ROLLBACK_FAILED: " + error.Message);
            }
        }

        private void Remember(ProjectInfo project, string contentId, string source, AiEngineeringView view, bool hit)
        {
            _cache = new Cache
            {
                Binding = Binding(project),
                ContentId = contentId,
                Epoch = _epoch,
                SourceDirectory = source,
                View = view,
                Hit = hit
            };
        }

        private static AiPatchRequest DeserializePatch(string canonical)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(canonical)))
                return (AiPatchRequest)new DataContractJsonSerializer(typeof(AiPatchRequest)).ReadObject(stream);
        }

        private string Envelope(ProjectInfo project, string contentId, bool cacheHit, string resultJson)
        {
            var binding = Binding(project);
            return "{" +
                "\"cacheHit\":" + (cacheHit ? "true" : "false") + "," +
                "\"targetBindingHash\":" + Quote(Sha(binding)) + "," +
                "\"bindingIdentity\":" + Quote(binding) + "," +
                "\"contentId\":" + Quote(contentId) + "," +
                "\"epoch\":" + _epoch.ToString(CultureInfo.InvariantCulture) + "," +
                "\"sourceKind\":" + Quote(project.SourceKind) + "," +
                "\"authority\":\"derived-not-build-input\"," +
                "\"projectIdentity\":{" +
                    "\"name\":" + Quote(project.Name) + "," +
                    "\"path\":" + Quote(project.Path) + "," +
                    "\"tiaVersion\":" + Quote(project.TiaVersion) +
                "}," +
                "\"result\":" + resultJson +
            "}";
        }

        private static ProjectInfo RequireSession(TiaProjectSession session)
        {
            if (session == null)
                throw new InvalidOperationException(
                    "No TIA Portal project is bound. Call connect_project or open_offline_project first.");
            return session.ReadProjectInfo();
        }

        private static void RequireOffline(TiaProjectSession session)
        {
            var project = RequireSession(session);
            if (!string.Equals(project.SourceKind, "offline-copy", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "AI engineering context requires an offline disposable project copy. Attached sessions are not exported.");
        }

        private static string ReadContentId(TiaProjectSession session)
        {
            var snapshot = session.ReadSnapshot();
            if (snapshot == null || snapshot.Capture == null ||
                !string.Equals(snapshot.Capture.Status, "complete", StringComparison.OrdinalIgnoreCase) ||
                snapshot.Project == null || string.IsNullOrWhiteSpace(snapshot.Project.ContentId))
                throw new InvalidOperationException(
                    "AI engineering context requires a complete snapshot contentId.");
            return snapshot.Project.ContentId;
        }

        private static string Binding(ProjectInfo project)
        {
            return string.Join("|", new[]
            {
                project.Name ?? string.Empty,
                project.Path ?? string.Empty,
                project.SourceKind ?? string.Empty,
                project.TiaVersion ?? string.Empty,
                project.ProcessId.HasValue
                    ? project.ProcessId.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty
            });
        }

        private static string Sha(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n") + "\"";
        }

        public sealed class IsolatedRoundTripResult
        {
            public bool Built { get; set; }
            public string ProjectFile { get; set; }
            public string Verdict { get; set; }
            public string BlockedCode { get; set; }
            public string Message { get; set; }
        }

        private sealed class Cache
        {
            public string Binding;
            public string ContentId;
            public int Epoch;
            public string SourceDirectory;
            public AiEngineeringView View;
            public bool Hit;
        }

        private sealed class AiQueryPayload
        {
            public string Kind { get; set; }
            public string Block { get; set; }
            public int Network { get; set; }
            public string Symbol { get; set; }
            public bool Force { get; set; }
        }

        private sealed class AiApplyPayload
        {
            public string PatchJson { get; set; }
            public string ExpectedContentId { get; set; }
            public string ExpectedFingerprint { get; set; }
            public int ExpectedEpoch { get; set; }
            public string InjectFailure { get; set; }
        }
    }
}
