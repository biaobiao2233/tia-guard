using System;
using System.IO;
using System.Reflection;

namespace TiaGuard.Openness
{
    public static class OpennessRuntime
    {
        private static readonly object Gate = new object();
        private static string[] _searchDirectories;

        public static string DefaultPublicApiDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"Siemens\Automation\Portal V21\PublicAPI\V21\net48");

        // Siemens binaries stay in the local TIA installation. The application
        // resolves them there at runtime instead of copying them into output.
        public static void Initialize(string publicApiDirectory = null)
        {
            lock (Gate)
            {
                var api = Path.GetFullPath(publicApiDirectory ?? DefaultPublicApiDirectory);
                if (_searchDirectories != null)
                {
                    if (!string.Equals(_searchDirectories[0], api, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The V21 PublicAPI directory is already configured differently.");
                    return;
                }
                var baseDll = Path.Combine(api, "Siemens.Engineering.Base.dll");
                var step7Dll = Path.Combine(api, "Siemens.Engineering.Step7.dll");
                if (!File.Exists(baseDll) || !File.Exists(step7Dll))
                    throw new FileNotFoundException("The TIA Portal V21 PublicAPI Base/Step7 DLLs are missing.", api);

                var portalRoot = Path.GetFullPath(Path.Combine(api, @"..\..\.."));
                _searchDirectories = new[] { api, Path.Combine(portalRoot, @"Bin\PublicAPI") };
                AppDomain.CurrentDomain.AssemblyResolve += ResolveSiemensAssembly;
                try
                {
                    Assembly.LoadFrom(baseDll);
                    Assembly.LoadFrom(step7Dll);
                }
                catch
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= ResolveSiemensAssembly;
                    _searchDirectories = null;
                    throw;
                }
            }
        }

        private static Assembly ResolveSiemensAssembly(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            var name = requested.Name;
            if (!name.StartsWith("Siemens.Engineering.", StringComparison.Ordinal) ||
                name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name.Contains(".."))
                return null;
            foreach (var directory in _searchDirectories)
            {
                var file = Path.Combine(directory, name + ".dll");
                if (File.Exists(file) &&
                    (requested.Version == null || requested.Version.Equals(AssemblyName.GetAssemblyName(file).Version)))
                    return Assembly.LoadFrom(file);
            }
            return null;
        }
    }
}
