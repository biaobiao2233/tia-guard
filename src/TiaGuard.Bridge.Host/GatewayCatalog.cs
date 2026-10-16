using System.Reflection;
using System.Text.Json;

namespace TiaGuard.Bridge.Host;

public static class GatewayCatalog
{
    public const string Product = "TIA-Guard AI Gateway";
    public const string Version = "0.1.0";
    public const string SchemaVersion = "1";

    public static string CapabilitiesJson(bool headlessWrite, bool simulationEnabled = false)
    {
        var document = new
        {
            product = Product,
            version = Version,
            gatewaySchemaVersion = SchemaVersion,
            bind = "127.0.0.1",
            port = 18761,
            read = new[]
            {
                "project-context",
                "program-graph",
                "network",
                "where-used",
                "refresh",
                "preview_project_generation"
            },
            write = new
            {
                available = true,
                authorization = headlessWrite ? "single-use-preview-token" : "one-shot-gui-approval",
                operations = new[] { "upsert_tag", "replace_output_condition", "build_project_generation" },
                supported = new[]
                {
                    "serial-and",
                    "ordinary-no-contact",
                    "negated-nc-read",
                    "ordinary-coil"
                }
            },
            projectGeneration = new
            {
                available = true,
                existingProjectRequired = false,
                source = "project-generation.json",
                preview = "/api/project-generation/preview",
                build = "/api/project-generation/build",
                inputHashBound = true,
                outputMustBeNew = true,
                attachedSessionAllowed = false,
                supportedSourceFormats = new[] { "SCL" },
                successRequires = new[] { "published", "complete-successful-compilation", "independent-verification-PASS" }
            },
            simulation = new
            {
                enabled = simulationEnabled, experimental = true, runtimeQualified = false,
                backend = "plcsim-v21-flaui-uia3", cpu = "CPU1212C/V4.7", io = "Bool I/Q",
                nativeRunIoQualified = false,
                enableWith = "--allow-write --enable-simulation", physicalPlcAllowed = false,
                status = "/api/simulation/status", create = "/api/simulation/create", load = "/api/simulation/load", test = "/api/simulation/test",
                run = "/api/simulation/run", inputs = "/api/simulation/inputs", scan = "/api/simulation/scan",
                outputs = "/api/simulation/outputs/{sessionId}", close = "/api/simulation/close",
                desktop = new { backend = "flaui-uia3", requiresBrowser = false, status = "/api/simulation/desktop/status",
                    open = "/api/simulation/desktop/open", inspect = "/api/simulation/desktop/{sessionId}",
                    act = "/api/simulation/desktop/act", close = "/api/simulation/desktop/close",
                    actions = new[] { "create", "rename", "open-details", "delete", "power-on", "power-off", "prepare-io" }, powerSupported = true, ioMonitoringQualified = false }
            },
            unsupported = new[]
            {
                "or",
                "arbitrary-parallel-graph-rewriting",
                "stateful-instructions",
                "unknown-graph",
                "s7-1500",
                "hmi",
                "safety",
                "drive",
                "online-plc-write",
                "download",
                "start-stop",
                "force"
            },
            authority = new
            {
                tiaSource = "authoritative",
                aiEngineering = "derived",
                patch = "mutation-intent"
            },
            safety = new
            {
                originalProjectWrite = false,
                offlineDisposableOnly = true,
                previewRequired = true,
                singleUseToken = true,
                compileRequired = true,
                roundTripVerifyRequired = true,
                semanticPostconditionRequired = true,
                localhostOnly = true
            }
        };
        return JsonSerializer.Serialize(document);
    }

    public static string OpenApiJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TiaGuard.Bridge.openapi.json")
            ?? throw new InvalidOperationException("The OpenAPI document is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
