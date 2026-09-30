using System;
using System.Globalization;
using System.IO;
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

        public void Invalidate()
        {
            _epoch++;
            _cache = null;
            DeleteOwnedDirectory(_root);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cache = null;
            DeleteOwnedDirectory(_root);
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
                DeleteOwnedDirectory(_cache.SourceDirectory);
            Remember(project, exportedContentId, source, view, false);
            return view;
        }

        private void DeleteOwnedDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var prefix = root + Path.DirectorySeparatorChar;
            if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to delete a directory outside the AI workspace.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
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

    }
}
