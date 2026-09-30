using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaGuard.Bridge.Host;

public sealed class BridgePatchCoordinator
{
    public const string Operation = "engineering_patch";
    private readonly IBridgeEngineeringGateway _gateway;
    private readonly BridgeWriteSafetyService _safety;
    private readonly BridgeAiContextService _context;

    public BridgePatchCoordinator(
        IBridgeEngineeringGateway gateway,
        BridgeWriteSafetyService safety,
        BridgeAiContextService context)
    {
        _gateway = gateway;
        _safety = safety;
        _context = context;
    }

    public async Task<string> PreviewAsync(string patchJson, CancellationToken cancellationToken = default)
    {
        var raw = await _gateway.PreviewPatchAsync(patchJson, cancellationToken).ConfigureAwait(false);
        var binding = PatchBinding.Read(raw);
        var ticket = _safety.CreatePreview(
            Operation,
            "Preview a structured engineering patch. The open project is not modified.",
            binding.BindingIdentity,
            binding.CanonicalPatch,
            binding.State,
            savesProject: false);
        var node = JsonNode.Parse(raw) as JsonObject
            ?? throw new InvalidOperationException("Patch preview was not a JSON object.");
        node["safetyToken"] = ticket.SafetyToken;
        node["expiresAt"] = ticket.ExpiresAt.ToString("O", CultureInfo.InvariantCulture);
        node["savesProject"] = false;
        node["publishes"] = false;
        return node.ToJsonString();
    }

    public async Task<string> ApplyAsync(
        string patchJson,
        string safetyToken,
        CancellationToken cancellationToken = default)
    {
        var fresh = await _gateway.PreviewPatchAsync(patchJson, cancellationToken).ConfigureAwait(false);
        var binding = PatchBinding.Read(fresh);
        _safety.ValidateAndConsume(
            safetyToken,
            Operation,
            binding.BindingIdentity,
            binding.CanonicalPatch,
            binding.State);
        try
        {
            return await _gateway.ApplyPatchAsync(
                patchJson,
                binding.ContentId,
                binding.Fingerprint,
                binding.Epoch,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _context.Invalidate();
        }
    }

    internal sealed class PatchBinding
    {
        public string BindingIdentity { get; init; } = string.Empty;
        public string ContentId { get; init; } = string.Empty;
        public string Fingerprint { get; init; } = string.Empty;
        public string CanonicalPatch { get; init; } = string.Empty;
        public int Epoch { get; init; }
        public string State => ContentId + "\n" + Fingerprint + "\n" +
            Epoch.ToString(CultureInfo.InvariantCulture);

        public static PatchBinding Read(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("result", out var result))
                throw new InvalidOperationException("Patch preview did not include a plan.");
            var identity = Required(root, "bindingIdentity");
            var contentId = Required(root, "contentId");
            var fingerprint = Required(result, "fingerprint");
            var canonical = Required(result, "canonicalPatch");
            if (!root.TryGetProperty("epoch", out var epoch) || !epoch.TryGetInt32(out var epochValue))
                throw new InvalidOperationException("Patch preview did not include an epoch.");
            return new PatchBinding
            {
                BindingIdentity = identity,
                ContentId = contentId,
                Fingerprint = fingerprint,
                CanonicalPatch = canonical,
                Epoch = epochValue
            };
        }

        private static string Required(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Patch preview is missing " + name + ".");
            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Patch preview is missing " + name + ".");
            return text;
        }
    }
}
