using System;
using System.Globalization;
using System.IO;
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
            try
            {
                if (request.Operation == "upsert_tag")
                {
                    session.UpsertRootTagForBridge(
                        request.Target.Table, request.Target.Tag, request.DataType, request.LogicalAddress);
                }
                else if (request.Operation == "replace_output_condition")
                {
                    session.ImportMainBlockForBridge(plan.ModifiedBlockXmlPath);
                }
                else
                {
                    throw new InvalidOperationException("AI_PATCH_REJECTED: OPERATION_UNSUPPORTED");
                }

                var compile = session.CompilePlcForVerification();
                if (compile.Errors != 0)
                {
                    Rollback(session, request, rollbackXml);
                    return RoundTripJson.Serialize(Failure(
                        request.Operation, compile, "compile errors")).Trim();
                }

                session.SaveDisposableCopyForBridge();
                string verified;
                try
                {
                    verified = ExportVerified(session);
                }
                catch (Exception error)
                {
                    Rollback(session, request, rollbackXml);
                    return RoundTripJson.Serialize(Failure(
                        request.Operation, compile, "canonical export was not deterministic: " + error.Message)).Trim();
                }

                var after = AiEngineeringRenderer.Read(verified);
                var result = Prove(request, plan, before, after, compile, ReadContentId(session));
                if (!string.Equals(result.Status, "applied", StringComparison.Ordinal))
                {
                    Rollback(session, request, rollbackXml);
                    result.Status = "verification_failure";
                    result.SavedDisposableCopy = false;
                    return RoundTripJson.Serialize(result).Trim();
                }

                if (!string.IsNullOrWhiteSpace(previousSource))
                    AiEngineeringPatch.DeleteOwnedDirectory(previousSource);
                Remember(RequireSession(session), result.ContentId, verified, after, false);
                return RoundTripJson.Serialize(result).Trim();
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

        private AiPatchApplyResult Prove(
            AiPatchRequest request, AiPatchPlan plan, AiEngineeringView before, AiEngineeringView after,
            PlcCompileObservation compile, string contentId)
        {
            try
            {
                AiEngineeringPatch.AssertNeighborsUnchanged(
                    before, after, request.Operation == "replace_output_condition" ? request.Target.Network : -1);
            }
            catch (InvalidOperationException)
            {
                return Failure(request.Operation, compile, "a neighboring network changed");
            }

            if (request.Operation == "upsert_tag")
            {
                var table = after.TagTables.Find(item =>
                    string.Equals(item.Name, request.Target.Table, StringComparison.Ordinal));
                var tag = table == null ? null : table.Tags.Find(item =>
                    string.Equals(item.Name, request.Target.Tag, StringComparison.Ordinal));
                if (tag == null ||
                    !string.Equals(tag.DataType, request.DataType, StringComparison.Ordinal) ||
                    !AddressesMatch(tag.Address, request.LogicalAddress))
                    return Failure(request.Operation, compile, "resulting tag does not match the preview");
                return Success(request.Operation, compile, contentId, null, new AiPatchTagState
                {
                    Table = table.Name,
                    Name = tag.Name,
                    Exists = true,
                    DataType = tag.DataType,
                    LogicalAddress = tag.Address,
                    SourceRef = tag.SourceRef
                });
            }

            var block = AiEngineeringContextQuery.ResolveBlock(after, request.Target.Block);
            var network = AiEngineeringContextQuery.ResolveNetwork(block, request.Target.Network);
            var expected = plan.ExpectedNetwork == null || plan.ExpectedNetwork.Analysis == null ||
                plan.ExpectedNetwork.Analysis.Writes.Count != 1
                ? null : plan.ExpectedNetwork.Analysis.Writes[0].Expression;
            var actual = network.Analysis == null || network.Analysis.Status != "supported" ||
                network.Analysis.Writes.Count != 1
                ? null : network.Analysis.Writes[0].Expression;
            if (!AiEngineeringPatch.ExpressionEquals(expected, actual))
                return Failure(request.Operation, compile, "resulting LAD semantics do not match the preview");
            return Success(request.Operation, compile, contentId, network, null);
        }

        private static void Rollback(TiaProjectSession session, AiPatchRequest request, string previousXml)
        {
            if (request.Operation != "replace_output_condition" || string.IsNullOrWhiteSpace(previousXml) ||
                !File.Exists(previousXml))
                return;
            try
            {
                session.ImportMainBlockForBridge(previousXml);
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

        private AiPatchApplyResult Success(
            string operation, PlcCompileObservation compile, string contentId,
            AiNetwork network, AiPatchTagState tag)
        {
            return new AiPatchApplyResult
            {
                Status = "applied",
                Operation = operation,
                SavedOriginalProject = false,
                SavedDisposableCopy = true,
                Published = false,
                CompileErrors = compile.Errors,
                CompileWarnings = compile.Warnings,
                VerifyVerdict = "pass",
                ContentId = contentId,
                ResultingNetwork = network,
                ResultingTag = tag,
                Epoch = _epoch
            };
        }

        private AiPatchApplyResult Failure(string operation, PlcCompileObservation compile, string reason)
        {
            return new AiPatchApplyResult
            {
                Status = "verification_failure",
                Operation = operation,
                SavedOriginalProject = false,
                SavedDisposableCopy = false,
                Published = false,
                CompileErrors = compile.Errors,
                CompileWarnings = compile.Warnings,
                VerifyVerdict = "fail",
                Reason = reason,
                Epoch = _epoch
            };
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

        private static bool AddressesMatch(string actual, string expected)
        {
            var left = SnapshotAddressParser.Parse(actual);
            var right = SnapshotAddressParser.Parse(expected);
            return left.ParseStatus == "parsed" && right.ParseStatus == "parsed" &&
                string.Equals(left.Area, right.Area, StringComparison.Ordinal) &&
                left.ByteOffset == right.ByteOffset && left.BitOffset == right.BitOffset &&
                left.BitWidth == right.BitWidth;
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
        }
    }
}
