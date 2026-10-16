#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaGuard.Bridge.Worker
{
    public enum PlcsimPowerDecision { Complete, Toggle, Wait }
    public sealed class PlcsimUiControl
    {
        public string Reference { get; set; } = "";
        public string? Name { get; set; }
        public string? AutomationId { get; set; }
        public string[] AncestorIds { get; set; } = Array.Empty<string>();
        public string Type { get; set; } = "";
        public bool Enabled { get; set; }
        public bool Offscreen { get; set; }
        public string[] Patterns { get; set; } = Array.Empty<string>();
        public string? Value { get; set; }
    }

    public static class PlcsimUiIdentity
    {
        public static string ObservationTimestamp(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Observation time must be UTC.");
            // Serialize a string explicitly: net48 JavaScriptSerializer otherwise
            // emits /Date(...)/, which the net8 proof reader cannot consume.
            return utc.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        }
        public static PlcsimPowerDecision DecidePower(bool desired, bool? pending, bool? observed)
        {
            // A delayed read may now prove that the previous intent completed.
            if (pending.HasValue && observed == pending) pending = null;
            if (pending.HasValue) {
                if (pending.Value != desired) throw new InvalidOperationException("Previous power transition is unresolved; inspect before requesting its opposite.");
                return PlcsimPowerDecision.Wait;
            }
            if (!observed.HasValue) throw new InvalidOperationException("Current power state is unknown; no toggle will be guessed.");
            return observed.Value == desired ? PlcsimPowerDecision.Complete : PlcsimPowerDecision.Toggle;
        }
        public static Guid[] InstanceIds(IEnumerable<PlcsimUiControl> controls)
        {
            var identities = new HashSet<Guid>();
            foreach (var key in controls.SelectMany(c => new[] { c.AutomationId }.Concat(c.AncestorIds)).Distinct())
            {
                if (key == null || !key.StartsWith("instance-name-", StringComparison.Ordinal) ||
                    key == "instance-name-input" || key == "instance-name-label") continue;
                Guid id;
                if (!Guid.TryParseExact(key.Substring("instance-name-".Length), "D", out id) || id == Guid.Empty)
                    throw new InvalidOperationException("Unrecognized instance identity; refusing mutation.");
                identities.Add(id);
            }
            return identities.OrderBy(id => id).ToArray();
        }

        public static int ResolveButton(IReadOnlyList<PlcsimUiControl> controls, string stableId, string? requiredAncestor = null)
        {
            var indices = Enumerable.Range(0, controls.Count).Where(i => controls[i].Type == "Button" &&
                controls[i].Enabled && !controls[i].Offscreen && controls[i].Patterns.Contains("Invoke") &&
                HasId(controls[i], stableId) && (requiredAncestor == null || HasId(controls[i], requiredAncestor))).ToArray();
            if (indices.Length != 1) throw new InvalidOperationException("Expected one available button under '" + stableId + "'; found " + indices.Length + ".");
            return indices[0];
        }

        public static bool HasId(PlcsimUiControl control, string id) => control.AutomationId == id || control.AncestorIds.Contains(id);
        public static int ResolveNavigationExpander(IReadOnlyList<PlcsimUiControl> controls)
        {
            var matches = Enumerable.Range(0, controls.Count).Where(i => controls[i].Type == "Button" &&
                controls[i].Name == "Unknown" && HasId(controls[i], "nav-bar") && !HasId(controls[i], "menu-tabs") &&
                controls[i].Enabled && !controls[i].Offscreen && controls[i].Patterns.Contains("Invoke")).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Collapsed navigation expander is not unique.");
            return matches[0];
        }
        public static void ValidateIoAddresses(string[] inputs, string[] outputs)
        {
            if (inputs == null || outputs == null || outputs.Length == 0 || inputs.Length > 64 || outputs.Length > 64 ||
                inputs.Distinct(StringComparer.Ordinal).Count() != inputs.Length || outputs.Distinct(StringComparer.Ordinal).Count() != outputs.Length ||
                inputs.Any(a => a == null || !Regex.IsMatch(a, @"\AI(?:0|[1-9][0-9]{0,3})\.[0-7]\z")) ||
                outputs.Any(a => a == null || !Regex.IsMatch(a, @"\AQ(?:0|[1-9][0-9]{0,3})\.[0-7]\z")))
                throw new ArgumentException("Declare at most 64 canonical Bool I and Q addresses; no output writes or arbitrary memory areas are supported.");
        }
        public static int ResolvePowerIcon(IReadOnlyList<PlcsimUiControl> controls, Guid instance)
        {
            var id = "instance-power-icon-" + instance.ToString("D");
            var matches = Enumerable.Range(0, controls.Count).Where(i => controls[i].AutomationId == id &&
                controls[i].Type == "Image" && controls[i].Enabled && !controls[i].Offscreen).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("The owned power control is transitioning or ambiguous.");
            return matches[0];
        }
        public static bool? ObservedPower(IReadOnlyList<PlcsimUiControl> controls, Guid instance, int ownedRuntimeCount)
        {
            if (ownedRuntimeCount < 0 || ownedRuntimeCount > 1) return null;
            try { ResolvePowerIcon(controls, instance); } catch (InvalidOperationException) { return null; }
            return ownedRuntimeCount == 1;
        }
        public static void ValidateName(string? name)
        {
            if (name == null || !Regex.IsMatch(name, @"\ATiaGuard_[A-Za-z0-9_]{1,50}\z"))
                throw new ArgumentException("Owned simulator names must be TiaGuard_ followed by 1–50 ASCII letters, digits or underscores.");
        }
        public static void RequireSingleton(IEnumerable<PlcsimUiControl> controls, Guid id, string name)
        {
            var frozen = controls.ToArray();
            if (!InstanceIds(frozen).SequenceEqual(new[] { id }) ||
                !frozen.Any(c => HasId(c, "instance-name-" + id.ToString("D")) && c.Name == name))
                throw new InvalidOperationException("Owned simulator identity/name or singleton count changed.");
        }
    }
}
