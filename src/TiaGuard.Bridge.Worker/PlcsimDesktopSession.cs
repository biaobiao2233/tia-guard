using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.UIA3;
using TiaGuard.Bridge.Host;

namespace TiaGuard.Bridge.Worker
{
    public sealed class PlcsimDesktopRequest
    {
        public string SessionId { get; set; }
        public string ExecutablePath { get; set; }
        public string Action { get; set; }
        public string Name { get; set; }
        public string[] InputAddresses { get; set; }
        public string[] OutputAddresses { get; set; }
    }

    // All calls are serialized by the existing Worker protocol. No arbitrary
    // window attach, scripts, physical targets, registry or global keyboard input.
    internal sealed partial class PlcsimDesktopSession
    {
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        private Process _application;
        private long _startTicks;
        private IntPtr _window;
        private UIA3Automation _automation;
        private AutomationElement[] _elements = Array.Empty<AutomationElement>();
        private PlcsimUiControl[] _controls = Array.Empty<PlcsimUiControl>();
        private Semaphore _lease;
        private Guid? _instance;
        private string _name;
        private bool _createRequested;
        private bool _closed;
        private string _executablePath;
        private bool? _powerPending;
        private readonly PlcsimOwnedRuntime _runtime = new PlcsimOwnedRuntime();
        private string _phase = "opening";
        private long _revision;
        private int _accessibilityRefreshes;
        private const int MaxControls = 6000;

        public object Open(string executablePath)
        {
            var path = Path.GetFullPath(executablePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Siemens", "Automation", "PLCSIM_V21", "S7PLCSIMV21.exe"));
            var version = FileVersionInfo.GetVersionInfo(path);
            if (Path.GetFileName(path) != "S7PLCSIMV21.exe" || version.ProductName != "S7PLCSIMV21" ||
                !(version.FileVersion ?? "").StartsWith("2100.", StringComparison.Ordinal) || !WindowsSignedApplication.IsSiemensSigned(path))
                throw new InvalidOperationException("A Siemens-signed installed S7PLCSIMV21.exe is required.");
            _lease = new Semaphore(1, 1, @"Local\TiaGuard.PLCSIM.Standard.V21");
            if (!_lease.WaitOne(0)) { _lease.Dispose(); _lease = null; throw new InvalidOperationException("Another Bridge owns the simulator adapter."); }
            var running = Process.GetProcessesByName("S7PLCSIMV21");
            try { if (running.Length != 0) throw new InvalidOperationException("Existing PLCSIM applications and their user instances are protected; use a fresh workspace."); }
            finally { foreach (var process in running) process.Dispose(); }
            var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, Arguments = "--force-renderer-accessibility --disable-renderer-backgrounding" };
            foreach (var key in start.EnvironmentVariables.Keys.Cast<string>().Where(k => k.Equals("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("DOTNET_ROOT(x86)", StringComparison.OrdinalIgnoreCase) || k.StartsWith("DOTNET_ROOT_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.EnvironmentVariables.Remove(key);
            _executablePath = path;
            _application = Process.Start(start) ?? throw new InvalidOperationException("PLCSIM did not start.");
            _startTicks = _application.StartTime.ToUniversalTime().Ticks;
            var timer = Stopwatch.StartNew();
            do { RequireApplication(); _window = PlcsimDesktopWindow.FindOwnedWindow(_application.Id); if (_window != IntPtr.Zero) break; Thread.Sleep(200); }
            while (timer.Elapsed < TimeSpan.FromSeconds(25));
            if (_window == IntPtr.Zero) throw new InvalidOperationException("Owned PLCSIM window did not appear.");
            PlcsimDesktopWindow.Prepare(_window, _application.Id);
            _automation = new UIA3Automation { ConnectionTimeout = TimeSpan.FromSeconds(2), TransactionTimeout = TimeSpan.FromSeconds(3) };
            EnsureInstancesPage();
            if (PlcsimUiIdentity.InstanceIds(_controls).Length != 0)
                throw new InvalidOperationException("The launched application restored existing instances; they are protected.");
            _phase = "open";
            return Snapshot();
        }

        public object State() => new { success = true, sessionId = SessionId, phase = _phase, closed = _closed,
            processId = _application == null ? (int?)null : _application.Id, instanceId = _instance, instanceName = _name,
            createRequested = _createRequested, backend = "flaui-uia3", runtimeQualified = false };
        public void RequireSession(string id)
        {
            if (_closed || id != SessionId) throw new InvalidOperationException("Unknown or closed desktop session.");
        }
        public object Inspect() { RequireApplication(); Capture(); return Snapshot(); }

        public object Act(PlcsimDesktopRequest request)
        {
            RequireSession(request.SessionId);
            switch (request.Action)
            {
                case "create": Create(request.Name ?? "TiaGuard_" + SessionId.Substring(0, 12)); break;
                case "rename": Rename(request.Name); break;
                case "open-details": OpenDetails(); break;
                case "delete": Delete(); break;
                case "power-on": Power(true); break;
                case "power-off": Power(false); break;
                case "prepare-io": PrepareIo(request.InputAddresses, request.OutputAddresses); break;
                default: throw new ArgumentException("Supported desktop actions: create, rename, open-details, delete, power-on, power-off, prepare-io.");
            }
            Capture();
            return Snapshot();
        }

        private void Create(string desiredName)
        {
            PlcsimUiIdentity.ValidateName(desiredName);
            if (_createRequested) throw new InvalidOperationException("Creation already requested; inspect or close the session instead of creating again.");
            EnsureInstancesPage();
            if (PlcsimUiIdentity.InstanceIds(_controls).Length != 0 || !_controls.Any(c => c.Name == "无仿真实例" || c.Name == "No simulated instances"))
                throw new InvalidOperationException("An empty simulator workspace is required.");
            var button = _elements[PlcsimUiIdentity.ResolveButton(_controls, "S7_1200-add-button")];
            _createRequested = true; _phase = "creating";
            button.Patterns.Invoke.Pattern.Invoke();
            WaitFor(() => {
                var ids = PlcsimUiIdentity.InstanceIds(_controls);
                if (ids.Length > 1) throw new InvalidOperationException("Concurrent instance creation detected; refusing adoption.");
                if (ids.Length == 0) return false;
                _instance = ids[0];
                _name = _controls.Where(c => PlcsimUiIdentity.HasId(c, "instance-name-" + ids[0].ToString("D")))
                    .Select(c => c.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                return !string.IsNullOrWhiteSpace(_name);
            }, "Unique created instance identity was not confirmed; creation will not be repeated.");
            Rename(desiredName);
            _phase = "instance-ready";
        }

        private void Rename(string name)
        {
            PlcsimUiIdentity.ValidateName(name);
            RequireOwned();
            if (_name == name) return;
            OpenDetails();
            Invoke("property-title-edit-icon", "instance-header-section");
            WaitFor(() => _controls.Any(c => PlcsimUiIdentity.HasId(c, "instance-name-input") && c.Type == "Edit"), "Instance-name editor did not appear.");
            var indices = Enumerable.Range(0, _controls.Length).Where(i => _controls[i].Type == "Edit" &&
                PlcsimUiIdentity.HasId(_controls[i], "instance-name-input") && _elements[i].Patterns.Value.IsSupported).ToArray();
            if (indices.Length != 1) throw new InvalidOperationException("Instance-name editor is not unique.");
            var editor = _elements[indices[0]];
            if (editor.Patterns.Value.Pattern.Value.ValueOrDefault != _name)
                throw new InvalidOperationException("Name editor changed concurrently; preserving the current value.");
            _phase = "renaming";
            editor.Patterns.Value.Pattern.SetValue(name);
            PlcsimDesktopWindow.Enter(_window, _application.Id);
            WaitFor(() => _controls.Any(c => PlcsimUiIdentity.HasId(c, "instance-name-" + _instance.Value.ToString("D")) && c.Name == name), "Renamed instance was not read back.");
            _name = name; _phase = "instance-ready";
        }

        private void OpenDetails()
        {
            RequireOwned();
            var matching = Enumerable.Range(0, _controls.Length).Where(i => _controls[i].Name == _name &&
                PlcsimUiIdentity.HasId(_controls[i], "instance-name-" + _instance.Value.ToString("D"))).ToArray();
            if (matching.Length != 1) throw new InvalidOperationException("Owned name control is not unique.");
            var bounds = _elements[matching[0]].BoundingRectangle;
            PlcsimDesktopWindow.DoubleClick(_window, _application.Id, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            WaitFor(() => _controls.Any(c => PlcsimUiIdentity.HasId(c, "instance-header-section") && c.Name == _name), "Owned instance details did not appear.");
        }

        private void Delete()
        {
            if (!_instance.HasValue) { EnsureInstancesPage(); if (PlcsimUiIdentity.InstanceIds(_controls).Length != 0) throw new InvalidOperationException("Unowned instances are protected."); return; }
            RequireOwned();
            Power(false);
            OpenDetails();
            Invoke("property-title-delete-icon", "instance-header-section");
            WaitFor(() => _controls.Any(c => PlcsimUiIdentity.HasId(c, "delete-confirmation-modal") &&
                (c.Name == "是否确定要删除 " + _name + "？" || c.Name == "Are you sure you want to delete " + _name + "?")), "Exact owned deletion question is missing.");
            RequireOwned();
            _phase = "deleting";
            Invoke("confirm-button", "delete-confirmation-modal");
            WaitFor(() => PlcsimUiIdentity.InstanceIds(_controls).Length == 0 &&
                _controls.Any(c => c.Name == "无仿真实例" || c.Name == "No simulated instances"), "Owned instance deletion was not confirmed.");
            _instance = null; _name = null; _ioPrepared = false; _phase = "open";
        }

        private int[] RuntimeIds() => _runtime.Observe(_application.Id, _startTicks, _executablePath);
        private void Power(bool desired)
        {
            RequireOwned();
            var observed = PlcsimUiIdentity.ObservedPower(_controls, _instance.Value, RuntimeIds().Length);
            var decision = PlcsimUiIdentity.DecidePower(desired, _powerPending, observed);
            if (decision == PlcsimPowerDecision.Complete) { _powerPending = null; _phase = "instance-ready"; return; }
            if (decision == PlcsimPowerDecision.Toggle) {
                var icon = _elements[PlcsimUiIdentity.ResolvePowerIcon(_controls, _instance.Value)];
                var bounds = icon.BoundingRectangle;
                _powerPending = desired; _phase = desired ? "powering-on" : "powering-off";
                PlcsimDesktopWindow.Click(_window, _application.Id, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            }
            // Never repeat a toggle after a timeout. A retry only reads its outcome.
            WaitFor(() => {
                PlcsimUiIdentity.RequireSingleton(_controls, _instance.Value, _name);
                return PlcsimUiIdentity.ObservedPower(_controls, _instance.Value, RuntimeIds().Length) == desired;
            }, "Owned virtual CPU power transition was not confirmed; the toggle will not be repeated.", 60);
            _powerPending = null; _phase = "instance-ready";
        }

        public object Close()
        {
            if (_closed) return new { success = true, closed = true, sessionId = SessionId };
            _phase = "closing";
            if (_application != null && !_application.HasExited)
            {
                if (_automation != null) Delete();
                else if (_createRequested) throw new InvalidOperationException("Creation is unresolved; refusing blind application cleanup.");
                RequireApplication();
                _application.Refresh();
                if (!_application.CloseMainWindow() || !_application.WaitForExit(10000))
                    throw new InvalidOperationException("Owned application did not acknowledge normal close.");
            }
            _automation?.Dispose(); _automation = null;
            _application?.Dispose(); _application = null;
            if (_lease != null) { _lease.Release(); _lease.Dispose(); _lease = null; }
            _closed = true; _phase = "closed";
            return new { success = true, closed = true, sessionId = SessionId };
        }

        private void Invoke(string id, string ancestor)
        {
            Capture();
            _elements[PlcsimUiIdentity.ResolveButton(_controls, id, ancestor)].Patterns.Invoke.Pattern.Invoke();
        }
        private void RequireOwned()
        {
            EnsureInstancesPage();
            if (!_instance.HasValue || string.IsNullOrWhiteSpace(_name)) throw new InvalidOperationException("No owned instance has been confirmed.");
            PlcsimUiIdentity.RequireSingleton(_controls, _instance.Value, _name);
        }
        private void EnsureNavigationExpanded()
        {
            Capture();
            if (_controls.Any(c => c.Type == "Button" && PlcsimUiIdentity.HasId(c, "instances-navigation-tab"))) return;
            var expander = PlcsimUiIdentity.ResolveNavigationExpander(_controls);
            _elements[expander].Patterns.Invoke.Pattern.Invoke();
            WaitFor(() => _controls.Any(c => c.Type == "Button" && PlcsimUiIdentity.HasId(c, "instances-navigation-tab")), "Expanded instance navigation did not appear.");
        }
        private void EnsureInstancesPage()
        {
            Capture();
            if (_controls.Any(c => PlcsimUiIdentity.HasId(c, "instances-in-workspace") || c.Name == "无仿真实例" || c.Name == "No simulated instances")) return;
            EnsureNavigationExpanded();
            Invoke("instances-navigation-tab", "nav-bar");
            WaitFor(() => _controls.Any(c => PlcsimUiIdentity.HasId(c, "instances-in-workspace") || c.Name == "无仿真实例" || c.Name == "No simulated instances"), "Instance overview did not become visible.");
        }
        private void RequireApplication()
        {
            if (_closed || _application == null || _application.HasExited || _application.ProcessName != "S7PLCSIMV21" ||
                _application.StartTime.ToUniversalTime().Ticks != _startTicks)
                throw new InvalidOperationException("Owned PLCSIM application is unavailable or changed.");
        }
        private void WaitFor(Func<bool> condition, string message, int seconds = 25)
        {
            var timer = Stopwatch.StartNew();
            do { Capture(); if (condition()) return; Thread.Sleep(200); } while (timer.Elapsed < TimeSpan.FromSeconds(seconds));
            throw new InvalidOperationException(message);
        }
        private void Capture()
        {
            RequireApplication();
            if (_automation == null) throw new InvalidOperationException("UI automation is not initialized.");
            var timer = Stopwatch.StartNew();
            Exception last = null;
            var refreshed = false;
            do {
              try {
                var current = PlcsimDesktopWindow.FindOwnedWindow(_application.Id);
                if (current == IntPtr.Zero) { Thread.Sleep(200); continue; }
                if (current != _window) { _window = current; PlcsimDesktopWindow.Prepare(_window, _application.Id); }
                var window = _automation.FromHandle(_window);
                var all = new[] { window }.Concat(window.FindAllDescendants()).ToArray();
                if (all.Length > MaxControls) throw new InvalidOperationException("UI snapshot exceeds the bounded control limit; no mutation was authorized.");
                if (all.Length <= 10) {
                    // Electron may discard its accessibility provider after a
                    // renderer transition. Re-request only the read tree, never
                    // replay the creation/power action that preceded it.
                    if (!refreshed) { PlcsimDesktopWindow.Prepare(_window, _application.Id); _accessibilityRefreshes++; refreshed = true; }
                    Thread.Sleep(200); continue;
                }
                var controls = all.Select((element, index) => new PlcsimUiControl {
                Reference = (_revision + 1) + ":" + index, Name = element.Properties.Name.ValueOrDefault,
                AutomationId = element.Properties.AutomationId.ValueOrDefault, Type = element.Properties.ControlType.ValueOrDefault.ToString(),
                AncestorIds = Ancestors(element), Enabled = element.Properties.IsEnabled.ValueOrDefault,
                Offscreen = element.Properties.IsOffscreen.ValueOrDefault,
                Patterns = new[] { element.Patterns.Invoke.IsSupported ? "Invoke" : null, element.Patterns.Value.IsSupported ? "Value" : null,
                    element.Patterns.Toggle.IsSupported ? "Toggle" : null, element.Patterns.SelectionItem.IsSupported ? "SelectionItem" : null }.Where(p => p != null).ToArray(),
                Value = element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value.ValueOrDefault : null
                }).ToArray();
                // Commit one complete frame. Never mix new handles with old descriptors.
                _elements = all; _controls = controls; _revision++; return;
              }
              catch (ElementNotAvailableException error) { last = error; }
              catch (PropertyNotSupportedException error) { last = error; }
              Thread.Sleep(200);
            } while (timer.Elapsed < TimeSpan.FromSeconds(15));
            throw new InvalidOperationException("A complete owned PLCSIM UI frame was not available. " + (last == null ? "Only the window shell was exposed." : last.GetType().Name), last);
        }
        private static string[] Ancestors(AutomationElement element)
        {
            var ids = new List<string>();
            for (var depth = 0; element != null && depth < 8; depth++, element = element.Parent) {
                var id = element.Properties.AutomationId.ValueOrDefault;
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return ids.ToArray();
        }
        private object Snapshot()
        {
            var runtimes = RuntimeIds();
            return new { success = true, sessionId = SessionId, phase = _phase, processId = _application.Id,
            observedAtUtc = PlcsimUiIdentity.ObservationTimestamp(DateTime.UtcNow), revision = _revision, accessibilityRefreshes = _accessibilityRefreshes, instanceId = _instance, instanceName = _name,
            instanceCount = _controls.Any(c => PlcsimUiIdentity.HasId(c, "instances-in-workspace") || c.Name == "无仿真实例" || c.Name == "No simulated instances") ? (int?)PlcsimUiIdentity.InstanceIds(_controls).Length : null,
            io = new { prepared = _ioPrepared, viewName = _ioView, tableId = _ioTable, inputAddresses = _ioInputs, outputAddresses = _ioOutputs, monitoring = false, valuesQualified = false },
            controls = _controls.Select(c => new {
                reference = c.Reference, name = c.Name, automationId = c.AutomationId, ancestorIds = c.AncestorIds,
                type = c.Type, enabled = c.Enabled, offscreen = c.Offscreen, patterns = c.Patterns, value = c.Value }).ToArray(),
            powered = _instance.HasValue ? PlcsimUiIdentity.ObservedPower(_controls, _instance.Value, runtimes.Length) : (bool?)null,
            powerPending = _powerPending, virtualCpuProcessIds = runtimes, powerEvidence = "owned-signed-runtime-process-and-stable-ui",
            supportedActions = new[] { "create", "rename", "open-details", "delete", "power-on", "power-off", "prepare-io" }, runtimeQualified = false };
        }
    }
}
