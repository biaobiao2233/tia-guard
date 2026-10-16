using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.Umac;
using Siemens.Engineering.Download;
using Siemens.Engineering.Download.Configurations;

namespace TiaGuard.Openness
{
    // Simulation operations always use the session's disposable project, never an
    // attached user project. The only communication adapter is local PLCSIM Softbus.
    public sealed partial class TiaProjectSession
    {
        private bool _temporarySimulationUserRequested;
        public string ReadSimulationBindingIdForBridge()
        {
            ThrowIfDisposed();
            return _scratchDirectory == null ? null : System.IO.Path.GetFileName(_scratchDirectory);
        }
        public object ReadSimulationSecurityForBridge(string projectBindingId)
        {
            var cpu = RequireSimulationCpu();
            RequireSimulationBinding(projectBindingId);
            var device = RequireSimulationDevice(cpu);
            var management = ((IEngineeringServiceProvider)_project).GetService<UmacConfigurator>();
            var umacDevice = device.GetService<UmacDevice>();
            if (management == null || umacDevice == null)
                throw new InvalidOperationException("Local PLC user-management metadata is unavailable.");
            return new { success = true, cpuName = cpu.Name, deviceName = device.Name,
                projectUserCount = management.ProjectUsers.Count, customRoleCount = management.CustomRoles.Count,
                availableDeviceFunctionRights = umacDevice.AvailableDeviceFunctionRights
                    .Select(right => new { right.Identifier, right.Name }).ToArray(),
                systemRoles = management.SystemRoles.Select(role => new { role.Identifier, role.Name,
                    rights = role.GetAssignedSystemDeviceFunctionRights(umacDevice)
                        .Select(right => right.Identifier).ToArray() }).ToArray() };
        }

        private static Device RequireSimulationDevice(DeviceItem cpu)
        {
            IEngineeringObject current = cpu;
            for (var depth = 0; depth < 20 && current is DeviceItem item; depth++) current = item.Parent;
            return current as Device ?? throw new InvalidOperationException("Owned CPU device is unavailable.");
        }
        public object PrepareSimulationForBridge(string projectBindingId, bool configureTemporaryPlcPassword = false,
            bool configureTemporaryPlcUser = false)
        {
            var cpu = RequireSimulationCpu();
            RequireSimulationBinding(projectBindingId);
            var settings = ((IEngineeringServiceProvider)_project).GetService<PlcSimulationSettingsProvider>();
            if (settings == null) throw new InvalidOperationException("Simulation compilation settings are unavailable.");
            settings.IsSimulationDuringBlockCompilationEnabled = true;
            var temporaryPasswordConfigured = false;
            if (configureTemporaryPlcPassword)
            {
                var security = cpu.GetService<PlcMasterSecretConfigurator>();
                if (security == null || security.MasterSecretConfiguration != MasterSecretConfiguration.WithoutPassword)
                    throw new InvalidOperationException("Temporary simulation password configuration requires the unconfigured default protection state; existing security settings are never overwritten.");
                // Explicit opt-in, owned offline copy only. Never log/export the password,
                // change an existing password, disable protection or call an online setter.
                using (var password = CreateTemporarySimulationPassword()) security.Protect(password);
                if (security.MasterSecretConfiguration != MasterSecretConfiguration.WithPassword)
                    throw new InvalidOperationException("Temporary PLC configuration password was not confirmed.");
                temporaryPasswordConfigured = true;
            }
            var temporaryUser = configureTemporaryPlcUser ? ConfigureTemporarySimulationUser(cpu) : null;
            // V21: DeviceItem compiles hardware; PlcSoftware compiles software only.
            // A successful block compile is not proof of a valid CPU configuration.
            var hardwareReport = CompilationService.Compile(cpu);
            var plc = (PlcSoftware)cpu.GetService<SoftwareContainer>().Software;
            var report = hardwareReport.IsSuccessful ? CompilationService.Compile(plc) : null;
            var successful = hardwareReport.IsSuccessful && report != null && report.IsSuccessful;
            if (successful) _project.Save();
            return new { success = successful,
                phase = successful ? "prepared" : hardwareReport.IsSuccessful ? "compile-software" : "compile-hardware",
                simulationSupport = settings.IsSimulationDuringBlockCompilationEnabled,
                temporaryPlcPasswordConfigured = temporaryPasswordConfigured,
                temporaryPlcUserConfigured = temporaryUser != null, temporaryPlcUser = temporaryUser,
                project = ReadProjectInfo(), disposableProjectPath = _project.Path.FullName,
                hardwareCompilation = hardwareReport, compilation = report };
        }

        private object ConfigureTemporarySimulationUser(DeviceItem cpu)
        {
            if (_temporarySimulationUserRequested)
                throw new InvalidOperationException("Temporary user creation was already requested; close this copy instead of replaying the mutation.");
            var device = RequireSimulationDevice(cpu);
            var management = ((IEngineeringServiceProvider)_project).GetService<UmacConfigurator>();
            var umacDevice = device.GetService<UmacDevice>();
            const string fullAccessId = "ProtectionLevelFullAccess";
            var fullAccess = umacDevice?.AvailableDeviceFunctionRights.Find(fullAccessId);
            if (management == null || fullAccess == null || fullAccess.Identifier != fullAccessId)
                throw new InvalidOperationException("The owned CPU's exact Full access right is unavailable.");
            // Preserve existing identities and rights. Never enable Anonymous, change an
            // existing password, add engineering rights, or assign a system admin role.
            var previousUsers = management.ProjectUsers.ToDictionary(user => user.Name,
                user => new { user.IsActive, roles = user.Roles.Select(role => role.Identifier).OrderBy(id => id, StringComparer.Ordinal).ToArray() });
            var previousRoles = management.CustomRoles.ToDictionary(role => role.Identifier,
                role => new { engineering = role.AssignedEngineeringRights.Select(right => right.Identifier).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    runtime = role.GetAssignedDeviceFunctionRights(umacDevice).Select(right => right.Identifier).OrderBy(id => id, StringComparer.Ordinal).ToArray() });
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            var userName = "TiaGuard_Sim_" + suffix;
            var roleName = "TiaGuard_Role_" + suffix;
            if (management.ProjectUsers.Find(userName) != null || management.CustomRoles.Find(roleName) != null)
                throw new InvalidOperationException("Temporary user/role name already exists.");
            _temporarySimulationUserRequested = true;
            var role = management.CustomRoles.Create(roleName, "Disposable TIA-Guard CPU compile test only");
            role.AssignDeviceFunctionRight(umacDevice, fullAccess);
            ProjectUser temporaryUser;
            using (var password = CreateTemporarySimulationPassword())
                temporaryUser = management.ProjectUsers.Create(userName, password);
            if (temporaryUser.IsActive) temporaryUser.Deactivate();
            // Remove only defaults from this newly created user, never from old users.
            foreach (var defaultRole in temporaryUser.Roles.ToArray()) temporaryUser.Roles.Remove(defaultRole);
            temporaryUser.Roles.Add(role);
            temporaryUser.Activate();
            var rights = role.GetAssignedDeviceFunctionRights(umacDevice);
            if (!temporaryUser.IsActive || temporaryUser.Roles.Count != 1 ||
                !temporaryUser.Roles.Contains(role) || role.AssignedEngineeringRights.Count != 0 ||
                rights.Count != 1 || rights[0].Identifier != fullAccessId ||
                management.ProjectUsers.Count != previousUsers.Count + 1 || management.CustomRoles.Count != previousRoles.Count + 1)
                throw new InvalidOperationException("Temporary CPU user/role readback did not match the requested scope.");
            foreach (var previous in previousUsers)
            {
                var user = management.ProjectUsers.Find(previous.Key);
                if (user == null || user.IsActive != previous.Value.IsActive ||
                    !user.Roles.Select(assigned => assigned.Identifier).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(previous.Value.roles))
                    throw new InvalidOperationException("An existing project user's state changed unexpectedly.");
            }
            foreach (var previous in previousRoles)
            {
                var existing = management.CustomRoles.Find(previous.Key);
                if (existing == null ||
                    !existing.AssignedEngineeringRights.Select(right => right.Identifier).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(previous.Value.engineering) ||
                    !existing.GetAssignedDeviceFunctionRights(umacDevice).Select(right => right.Identifier).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(previous.Value.runtime))
                    throw new InvalidOperationException("An existing role's rights changed unexpectedly.");
            }
            return new { userName, roleName, cpuName = cpu.Name, deviceName = device.Name,
                rightIdentifier = fullAccessId, active = true, engineeringRights = 0,
                existingUsersPreserved = true, existingRolesPreserved = true };
        }

        private static SecureString CreateTemporarySimulationPassword()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            var random = new byte[24];
            var password = new SecureString();
            try
            {
                using (var generator = RandomNumberGenerator.Create()) generator.GetBytes(random);
                foreach (var value in "Aa1!") password.AppendChar(value);
                foreach (var value in random) password.AppendChar(alphabet[value & 63]);
                password.MakeReadOnly();
                return password;
            }
            catch { password.Dispose(); throw; }
            finally { Array.Clear(random, 0, random.Length); }
        }

        public object LoadSimulationForBridge(SimulationTargetProof proof)
        {
            var cpu = RequireSimulationCpu();
            if (proof == null) throw new ArgumentException("Simulation target proof is required.");
            RequireSimulationBinding(proof.ProjectBindingId);
            proof.RequireFresh();
            var settings = ((IEngineeringServiceProvider)_project).GetService<PlcSimulationSettingsProvider>();
            if (settings == null || !settings.IsSimulationDuringBlockCompilationEnabled)
                throw new InvalidOperationException("Prepare and compile the disposable project for simulation first.");
            // Recompile at the actual load boundary, rejecting stale/failed preparation.
            var hardwareReport = CompilationService.Compile(cpu);
            if (!hardwareReport.IsSuccessful)
                return new { success = false, phase = "compile-hardware", hardwareCompilation = hardwareReport };
            var report = CompilationService.Compile((PlcSoftware)cpu.GetService<SoftwareContainer>().Software);
            if (!report.IsSuccessful)
                return new { success = false, phase = "compile-software", hardwareCompilation = hardwareReport, compilation = report };
            var provider = cpu.GetService<DownloadProvider>();
            if (provider == null) throw new InvalidOperationException("Download service is unavailable.");
            var mode = provider.Configuration.Modes.Find("PN/IE");
            var pc = mode?.PcInterfaces.Find("PLCSIM", 1);
            if (pc == null || pc.Name != "PLCSIM" || pc.Number != 1 || pc.TargetInterfaces.Count != 1)
                throw new InvalidOperationException("An unambiguous local PLCSIM interface is required; physical adapters are forbidden.");
            var accessible = pc.GetAccessibleDevices().ToList();
            if (accessible.Count != 1 || accessible[0].Address != proof.Address)
                throw new InvalidOperationException("PLCSIM discovery must identify exactly the owned simulation address.");
            proof.RequireFresh();
            var target = pc.TargetInterfaces[0];
            var targetInfo = new { mode = mode.Name, pcInterface = pc.Name, pcInterfaceNumber = pc.Number,
                targetInterface = target.Name, address = proof.Address,
                addressSelection = "verified-online-address",
                discovered = accessible.Select(d => new { d.Name, d.Address, series = d.DeviceSeries.ToString(), d.MACAddress }).ToArray() };
            var prompts = new List<string>();
            Action<DownloadConfiguration> configure = configuration => {
                prompts.Add(configuration.GetType().Name); ConfigureSimulationDownload(configuration);
            };
            try
            {
                var result = provider.Download(target, target.Addresses.Create(proof.Address),
                    configuration => configure(configuration), configuration => configure(configuration),
                    DownloadOptions.Hardware | DownloadOptions.Software);
                return new { success = result.ErrorCount == 0 && result.State.ToString() == "Success", phase = "load",
                    instanceId = proof.InstanceId, target = targetInfo, prompts, hardwareCompilation = hardwareReport, compilation = report,
                    state = result.State.ToString(), errors = result.ErrorCount,
                    warnings = result.WarningCount, messages = SimulationDownloadMessages(result.Messages).ToArray() };
            }
            catch (EngineeringException error)
            {
                return new { success = false, phase = "load", instanceId = proof.InstanceId, target = targetInfo,
                    prompts, hardwareCompilation = hardwareReport, compilation = report,
                    errorType = error.GetType().Name, error = error.ToString() };
            }
        }

        private void RequireSimulationBinding(string expected)
        {
            if (string.IsNullOrWhiteSpace(expected) ||
                !string.Equals(expected, ReadSimulationBindingIdForBridge(), StringComparison.Ordinal))
                throw new InvalidOperationException("The owned simulation project binding changed.");
        }
        private DeviceItem RequireSimulationCpu()
        {
            ThrowIfDisposed();
            if (_scratchDirectory == null || _sourcePath == null || _processId.HasValue)
                throw new InvalidOperationException("Simulation requires an owned offline disposable project copy.");
            var cpus = new List<DeviceItem>();
            foreach (var device in _project.Devices) CollectSimulationCpus(device.DeviceItems, cpus);
            if (cpus.Count != 1 || cpus[0].GetAttribute("TypeIdentifier") as string != RoundTripProfile.CpuTypeIdentifier)
                throw new InvalidOperationException("Simulation currently supports exactly one CPU 1212C / V4.7.");
            return cpus[0];
        }
        private static void CollectSimulationCpus(DeviceItemComposition items, List<DeviceItem> cpus)
        {
            foreach (var item in items) {
                if (item.GetService<SoftwareContainer>()?.Software is PlcSoftware) cpus.Add(item);
                CollectSimulationCpus(item.DeviceItems, cpus);
            }
        }
        private static IEnumerable<object> SimulationDownloadMessages(DownloadResultMessageComposition messages)
        {
            foreach (var m in messages) {
                yield return new { message = m.Message, state = m.State.ToString(), errors = m.ErrorCount, warnings = m.WarningCount };
                foreach (var child in SimulationDownloadMessages(m.Messages)) yield return child;
            }
        }
        private static void ConfigureSimulationDownload(DownloadConfiguration configuration)
        {
            if (configuration is StopModules stop) { stop.CurrentSelection = StopModulesSelections.StopAll; return; }
            if (configuration is StartModules start) { start.CurrentSelection = StartModulesSelections.NoAction; return; }
            if (configuration is CheckBeforeDownload check) { check.Checked = true; return; }
            if (configuration is AllBlocksDownload all) { all.CurrentSelection = AllBlocksDownloadSelections.DownloadAllBlocks; return; }
            if (configuration is ConsistentBlocksDownload consistent) { consistent.CurrentSelection = ConsistentBlocksDownloadSelections.ConsistentDownload; return; }
            if (configuration is OverwriteSystemData overwrite) { overwrite.CurrentSelection = OverwriteSystemDataSelections.Overwrite; return; }
            if (configuration is InitializeMemory memory) { memory.CurrentSelection = InitializeMemorySelections.AcceptAll; return; }
            if (configuration.GetType() == typeof(DownloadConfiguration)) return;
            throw new NotSupportedException("Unsupported simulator download prompt: " + configuration.GetType().Name);
        }
    }

    public sealed class SimulationTargetProof
    {
        public string ProjectBindingId { get; set; }
        public string InstanceId { get; set; }
        public string InstanceName { get; set; }
        public string Address { get; set; }
        public DateTime ObservedAtUtc { get; set; }
        public int InstanceCount { get; set; }
        public bool Powered { get; set; }
        public string Kind { get; set; }
        public void RequireFresh()
        {
            Guid identity;
            // net48 JavaScriptSerializer converts an ISO timestamp with Z into
            // Local. Normalize its represented instant instead of rejecting it.
            var observed = ObservedAtUtc.ToUniversalTime();
            if (!Guid.TryParse(InstanceId, out identity) || identity == Guid.Empty ||
                string.IsNullOrWhiteSpace(InstanceName) || !InstanceName.StartsWith("TiaGuard_", StringComparison.Ordinal) ||
                Address != "192.168.0.1" || InstanceCount != 1 || !Powered || Kind != "STANDARD S7-1200" ||
                ObservedAtUtc.Kind == DateTimeKind.Unspecified || DateTime.UtcNow - observed > TimeSpan.FromSeconds(20) ||
                observed > DateTime.UtcNow.AddSeconds(1))
                throw new InvalidOperationException("A fresh, owned singleton powered Standard S7-1200 target proof is required.");
        }
    }
}
