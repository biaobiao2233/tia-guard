using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;
using TiaGuard.Openness;

namespace TiaGuard.Bridge.Worker
{
    internal static class Program
    {
        private const string ProtocolVersion = "tia-guard.bridge.worker/v1";
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static TiaProjectSession _session;
        private static ProjectInfo _binding;
        private static bool _allowWrite;
        private static readonly BridgeAiWorkspace Ai = new BridgeAiWorkspace();

        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "--isolated-roundtrip", StringComparison.Ordinal))
                return BridgeAiWorkspace.RunIsolatedRoundTrip(args);

            foreach (var arg in args ?? Array.Empty<string>())
            {
                if (string.Equals(arg, "--allow-write", StringComparison.OrdinalIgnoreCase))
                    _allowWrite = true;
                else
                {
                    Console.Error.WriteLine("Unknown worker argument: " + arg);
                    return 2;
                }
            }

            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = new UTF8Encoding(false);

            string line;
            while ((line = Console.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                WorkerRequest request = null;
                WorkerResponse response;
                try
                {
                    request = Json.Deserialize<WorkerRequest>(line);
                    response = Dispatch(request);
                }
                catch (OpennessAccessException error)
                {
                    response = Failure(request?.Id, "openness_access", error.Message);
                }
                catch (ArgumentException error)
                {
                    response = Failure(request?.Id, "validation_error", error.Message);
                }
                catch (InvalidOperationException error)
                {
                    response = Failure(request?.Id, "invalid_state", error.Message);
                }
                catch (Exception error)
                {
                    response = Failure(request?.Id, "worker_error", error.Message);
                }

                Console.WriteLine(Json.Serialize(response));
                Console.Out.Flush();
            }

            DisposeSession();
            Ai.Dispose();
            return 0;
        }

        private static WorkerResponse Dispatch(WorkerRequest request)
        {
            if (request == null)
                return Failure(null, "validation_error", "Worker request is required.");

            if (!string.Equals(request.ProtocolVersion, ProtocolVersion, StringComparison.Ordinal))
                return Failure(request.Id, "protocol_mismatch",
                    "Unsupported worker protocol version.");

            switch ((request.Method ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "hello":
                    var capabilities = new List<string>
                    {
                        "doctor",
                        "list_open_projects",
                        "connect",
                        "open_offline_project",
                        "disconnect",
                        "get_state",
                        "get_project",
                        "get_project_snapshot",
                        "get_tag_state",
                        "get_ai_context_identity",
                        "query_ai",
                        "refresh_ai_context"
                    };
                    if (_allowWrite)
                    {
                        capabilities.Add("upsert_tag");
                        capabilities.Add("publish_offline_copy");
                        capabilities.Add("preview_engineering_patch");
                        capabilities.Add("apply_engineering_patch");
                    }
                    return Success(request.Id, Json.Serialize(new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities,
                        accessMode = _allowWrite ? "read-write" : "read-only"
                    }));

                case "doctor":
                    return Success(request.Id,
                        RoundTripJson.Serialize(OpennessEnvironmentProbe.Inspect()).Trim());

                case "list_open_projects":
                    return Success(request.Id,
                        Json.Serialize(TiaPortalDiscovery.ListOpenV21Projects()));

                case "connect":
                    return Connect(request);

                case "open_offline_project":
                    return OpenOfflineProject(request);

                case "disconnect":
                    DisposeSession();
                    return Success(request.Id, Json.Serialize(new
                    {
                        connected = false,
                        accessMode = _allowWrite ? "read-write" : "read-only"
                    }));

                case "get_state":
                    return Success(request.Id, BuildStatePayload());

                case "get_project":
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    return Success(request.Id, Json.Serialize(_session.ReadProjectInfo()));

                case "get_project_snapshot":
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    return Success(request.Id, SnapshotV1Json.Serialize(_session.ReadSnapshot()));

                case "get_tag_state":
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    return Success(request.Id, Json.Serialize(
                        _session.ReadRootTagStateForBridge(
                            request.TableName, request.TagName)));

                case "upsert_tag":
                    if (!_allowWrite)
                        return Failure(request.Id, "write_disabled",
                            "Bridge write mode is disabled. Restart with --allow-write.");
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    var upserted = _session.UpsertRootTagForBridge(
                        request.TableName,
                        request.TagName,
                        request.DataType,
                        request.LogicalAddress);
                    Ai.Invalidate();
                    return Success(request.Id, Json.Serialize(upserted));

                case "get_ai_context_identity":
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    return Success(request.Id, Ai.Identity(_session));

                case "query_ai":
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    return Success(request.Id, Ai.Query(_session, request.PayloadJson));

                case "refresh_ai_context":
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    return Success(request.Id, Ai.Query(_session,
                        "{\"Kind\":\"project\",\"Force\":true}"));

                case "preview_engineering_patch":
                    if (!_allowWrite)
                        return Failure(request.Id, "write_disabled",
                            "Bridge write mode is disabled. Restart with --allow-write.");
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    return Success(request.Id, Ai.PreviewPatch(_session, request.PayloadJson));

                case "apply_engineering_patch":
                    if (!_allowWrite)
                        return Failure(request.Id, "write_disabled",
                            "Bridge write mode is disabled. Restart with --allow-write.");
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    return Success(request.Id, Ai.ApplyPatch(_session, request.PayloadJson));

                case "publish_offline_copy":
                    if (!_allowWrite)
                        return Failure(request.Id, "write_disabled",
                            "Bridge write mode is disabled. Restart with --allow-write.");
                    EnsureConnected();
                    EnsureBindingStillMatches();
                    try
                    {
                        var published = _session.PublishOfflineCopyForBridge(
                            request.OutputDirectory,
                            request.OutputName);
                        return Success(request.Id, Json.Serialize(published));
                    }
                    finally
                    {
                        // SaveAs changes the live Project handle to the newly published
                        // project. End this worker binding immediately instead of letting a
                        // caller accidentally continue under the original logical identity.
                        DisposeSession();
                    }

                default:
                    return Failure(request.Id, "method_not_found",
                        "Unknown worker method: " + request.Method);
            }
        }

        private static WorkerResponse Connect(WorkerRequest request)
        {
            if (_session != null)
            {
                EnsureBindingStillMatches();
                if (!request.ProcessId.HasValue ||
                    request.ProcessId.Value == _binding.ProcessId)
                    return Success(request.Id, Json.Serialize(_binding));

                return Failure(request.Id, "binding_conflict",
                    "A different TIA project is already bound. Disconnect explicitly before switching projects.");
            }

            try
            {
                _session = TiaProjectSession.Attach(request.ProcessId);
                _binding = _session.ReadProjectInfo();
                return Success(request.Id, Json.Serialize(_binding));
            }
            catch
            {
                DisposeSession();
                throw;
            }
        }

        private static WorkerResponse OpenOfflineProject(WorkerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectPath))
                return Failure(request.Id, "validation_error",
                    "An offline .ap21 project path is required.");

            if (_session != null)
            {
                EnsureBindingStillMatches();
                if (string.Equals(_binding.Path, request.ProjectPath,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_binding.SourceKind, "offline-copy",
                        StringComparison.OrdinalIgnoreCase))
                    return Success(request.Id, Json.Serialize(_binding));

                return Failure(request.Id, "binding_conflict",
                    "A different TIA project is already bound. Disconnect explicitly before switching projects.");
            }

            try
            {
                _session = TiaProjectSession.OpenOfflineCopy(request.ProjectPath);
                _binding = _session.ReadProjectInfo();
                return Success(request.Id, Json.Serialize(_binding));
            }
            catch
            {
                DisposeSession();
                throw;
            }
        }

        private static string BuildStatePayload()
        {
            if (_session == null)
            {
                return Json.Serialize(new
                {
                    connected = false,
                    accessMode = _allowWrite ? "read-write" : "read-only",
                    project = (object)null
                });
            }

            try
            {
                EnsureBindingStillMatches();
                return Json.Serialize(new
                {
                    connected = true,
                    accessMode = _allowWrite ? "read-write" : "read-only",
                    project = _session.ReadProjectInfo()
                });
            }
            catch
            {
                DisposeSession();
                throw;
            }
        }

        private static void EnsureConnected()
        {
            if (_session == null)
                throw new InvalidOperationException(
                    "No TIA Portal project is bound. Call connect_project first.");
        }

        private static void EnsureBindingStillMatches()
        {
            EnsureConnected();
            var current = _session.ReadProjectInfo();
            if (_binding == null ||
                current.ProcessId != _binding.ProcessId ||
                !string.Equals(current.Path, _binding.Path, StringComparison.OrdinalIgnoreCase))
            {
                DisposeSession();
                throw new InvalidOperationException(
                    "The bound TIA project identity changed. The bridge session was invalidated; reconnect explicitly.");
            }
        }

        private static void DisposeSession()
        {
            try
            {
                _session?.Dispose();
            }
            finally
            {
                _session = null;
                _binding = null;
                Ai.Invalidate();
            }
        }

        private static WorkerResponse Success(string id, string payloadJson)
        {
            return new WorkerResponse
            {
                ProtocolVersion = ProtocolVersion,
                Id = id,
                Success = true,
                PayloadJson = payloadJson
            };
        }

        private static WorkerResponse Failure(string id, string category, string message)
        {
            return new WorkerResponse
            {
                ProtocolVersion = ProtocolVersion,
                Id = id,
                Success = false,
                ErrorCategory = category,
                Error = message
            };
        }
    }

    internal sealed class WorkerRequest
    {
        public string ProtocolVersion { get; set; }
        public string Id { get; set; }
        public string Method { get; set; }
        public int? ProcessId { get; set; }
        public string ProjectPath { get; set; }
        public string TableName { get; set; }
        public string TagName { get; set; }
        public string DataType { get; set; }
        public string LogicalAddress { get; set; }
        public string OutputDirectory { get; set; }
        public string OutputName { get; set; }
        public string PayloadJson { get; set; }
    }

    internal sealed class WorkerResponse
    {
        public string ProtocolVersion { get; set; }
        public string Id { get; set; }
        public bool Success { get; set; }
        public string PayloadJson { get; set; }
        public string ErrorCategory { get; set; }
        public string Error { get; set; }
    }
}
