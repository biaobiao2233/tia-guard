using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaGuard.Bridge.Worker
{
    internal sealed partial class PlcsimDesktopSession
    {
        private bool _ioRequested, _ioPrepared;
        private string _ioView, _ioTable;
        private string[] _ioInputs = Array.Empty<string>(), _ioOutputs = Array.Empty<string>();

        // Offline SIM table configuration only. This does not modify I/Q values.
        private void PrepareIo(string[] inputs, string[] outputs)
        {
            PlcsimUiIdentity.ValidateIoAddresses(inputs, outputs);
            RequireOwned();
            if (_ioRequested) throw new InvalidOperationException("SIM table creation was already requested; inspect the existing outcome rather than creating another.");
            EnsureNavigationExpanded();
            var before = ViewNames();
            _ioRequested = true;
            InvokeNamed(new[] { "新建 SIM 视图", "New SIM View" }, "menu-tabs");
            WaitFor(() => {
                var added = ViewNames().Except(before, StringComparer.Ordinal).ToArray();
                if (added.Length > 1) throw new InvalidOperationException("Concurrent SIM view creation detected; refusing adoption.");
                if (added.Length == 0) return false;
                _ioView = added[0]; return true;
            }, "Unique new SIM view was not read back.");
            InvokeNamed(new[] { _ioView }, "menu-tabs");
            WaitFor(() => _controls.Any(c => PlcsimUiIdentity.HasId(c, "simulation-page-empty-state")), "New SIM view did not expose its empty workspace.");
            Invoke("sim-table-add-button", "simulation-library-panel");
            WaitFor(() => {
                var ids = _controls.Select(c => c.AutomationId).Where(id => id != null && id.StartsWith("address-header-", StringComparison.Ordinal))
                    .Select(id => id.Substring("address-header-".Length)).Distinct().ToArray();
                if (ids.Length > 1) throw new InvalidOperationException("Unexpected additional SIM tables; refusing adoption.");
                Guid parsed;
                if (ids.Length != 1 || !Guid.TryParseExact(ids[0], "D", out parsed) || parsed == Guid.Empty) return false;
                _ioTable = ids[0]; return true;
            }, "New SIM table identity was not confirmed.");
            var properties = Enumerable.Range(0, _controls.Length).Where(i => _controls[i].AutomationId == "simulation-properties-tab" &&
                _controls[i].Enabled && !_controls[i].Offscreen && _controls[i].Patterns.Contains("Invoke")).ToArray();
            if (properties.Length != 1) throw new InvalidOperationException("SIM properties tab is not unique.");
            _elements[properties[0]].Patterns.Invoke.Pattern.Invoke();
            WaitFor(() => _controls.Any(c => c.Type == "Button" && PlcsimUiIdentity.HasId(c, "instance-binding-dropdown")), "SIM table binding selector did not appear.");
            Invoke("instance-binding-dropdown", "simulation-properties-panel");
            WaitFor(() => _controls.Any(c => c.Type == "Button" && c.Name == _name && PlcsimUiIdentity.HasId(c, "instance-binding-dropdown")), "Owned instance binding option did not appear.");
            InvokeNamed(new[] { _name }, "instance-binding-dropdown");
            WaitFor(BoundIoVisible, "Owned table binding was not confirmed.");
            foreach (var address in inputs.Concat(outputs)) AddIoAddress(address);
            _ioInputs = inputs.ToArray(); _ioOutputs = outputs.ToArray();
            _ioPrepared = true;
        }
        private string[] ViewNames() => _controls.Where(c => c.Type == "Button" && PlcsimUiIdentity.HasId(c, "menu-tabs") &&
            Regex.IsMatch(c.Name ?? "", @"\ASimView_[0-9]+\z")).Select(c => c.Name).Distinct().ToArray();
        private void InvokeNamed(string[] names, string ancestor)
        {
            Capture();
            var indices = Enumerable.Range(0, _controls.Length).Where(i => _controls[i].Type == "Button" && names.Contains(_controls[i].Name) &&
                PlcsimUiIdentity.HasId(_controls[i], ancestor) && _controls[i].Enabled && !_controls[i].Offscreen && _controls[i].Patterns.Contains("Invoke")).ToArray();
            if (indices.Length != 1) throw new InvalidOperationException("Named semantic control is not unique in its required region.");
            _elements[indices[0]].Patterns.Invoke.Pattern.Invoke();
        }
        private bool BoundIoVisible() => _ioTable != null &&
            _controls.Any(c => c.AutomationId == "address-header-" + _ioTable) &&
            _controls.Any(c => c.Type == "Button" && c.Name == _name && PlcsimUiIdentity.HasId(c, "instance-binding-dropdown"));
        private int[] EmptyAddressEditors() => Enumerable.Range(0, _controls.Length).Where(i => _controls[i].Type == "Edit" &&
            (_controls[i].AutomationId ?? "").StartsWith("address-input-", StringComparison.Ordinal) && PlcsimUiIdentity.HasId(_controls[i], _ioTable) &&
            _controls[i].Value == "" && _controls[i].Enabled && !_controls[i].Offscreen && _controls[i].Patterns.Contains("Value")).ToArray();
        private void AddIoAddress(string address)
        {
            Capture();
            if (!BoundIoVisible()) throw new InvalidOperationException("Owned SIM table binding changed; no address will be edited.");
            if (EmptyAddressEditors().Length == 0) {
                InvokeNamed(new[] { "Plus" }, _ioTable);
                WaitFor(() => EmptyAddressEditors().Length == 1, "New empty SIM row was not read back.");
            }
            var indices = EmptyAddressEditors();
            if (indices.Length != 1) throw new InvalidOperationException("Empty address editor is ambiguous.");
            var id = _controls[indices[0]].AutomationId;
            var editor = _elements[indices[0]];
            editor.Focus(); editor.Patterns.Value.Pattern.SetValue(address + ":P");
            PlcsimDesktopWindow.Enter(_window, _application.Id);
            WaitFor(() => BoundIoVisible() && _controls.Any(c => c.AutomationId == id && c.Value == address + ":P"), "SIM address was not read back.");
        }
    }
}
