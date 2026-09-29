using System;
using System.Reflection;
using TiaGuard.Openness;

namespace TiaGuard.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args != null && args.Length == 1 &&
                (args[0] == "--help" || args[0] == "-h" || args[0] == "help"))
            {
                Console.WriteLine(CliCommandLine.Usage);
                return 0;
            }

            if (args != null && args.Length == 1 &&
                (args[0] == "--version" || args[0] == "version"))
            {
                Console.WriteLine("tia-guard " + ProductVersion);
                return 0;
            }

            if (!CliCommandLine.TryParse(args, out var invocation))
            {
                Console.Error.WriteLine(CliCommandLine.Usage);
                return 2;
            }

            try
            {
                if (invocation.Command == "ai-view")
                {
                    Console.WriteLine("aiDirectory=" + AiEngineeringPublisher.Generate(invocation.Source));
                    return 0;
                }

                if (invocation.Command == "doctor")
                {
                    var report = OpennessEnvironmentProbe.Inspect();
                    Console.Write(RoundTripJson.Serialize(report));
                    return report.Ready ? 0 : 5;
                }

                if (invocation.Command == "export")
                {
                    using (var session = TiaProjectSession.OpenOfflineCopy(invocation.Source))
                    {
                        var manifest = session.ExportRoundTripSource(invocation.Target,
                            stage => Console.Error.WriteLine("stage=" + stage));
                        Console.Write(RoundTripJson.Serialize(manifest));
                        return manifest.RoundTripReady ? 0 : 5;
                    }
                }

                if (invocation.Command == "build")
                {
                    var result = RoundTripBuilder.Build(invocation.Source, invocation.Target,
                        stage => Console.Error.WriteLine("stage=" + stage));
                    Console.WriteLine("projectFile=" + result.ProjectFile);
                    Console.WriteLine("compileErrors=" + result.CompileErrors);
                    Console.WriteLine("compileWarnings=" + result.CompileWarnings);
                    return 0;
                }

                var verify = RoundTripVerifier.VerifyProjects(invocation.Source, invocation.Target,
                    stage => Console.Error.WriteLine("stage=" + stage));
                Console.Write(RoundTripJson.Serialize(verify));
                return verify.Verdict == "pass" ? 0 :
                    verify.Verdict == "mismatch" ? 4 : 5;
            }
            catch (OpennessAccessException error)
            {
                Console.Error.WriteLine("error=" + error.Message);
                return 3;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("error=" + error.Message);
                return 2;
            }
        }

        private static string ProductVersion
        {
            get
            {
                var attribute = typeof(Program).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var version = attribute?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    var metadata = version.IndexOf('+');
                    return metadata >= 0 ? version.Substring(0, metadata) : version;
                }

                var assemblyVersion = typeof(Program).Assembly.GetName().Version;
                return assemblyVersion == null
                    ? "0.1.0"
                    : assemblyVersion.Major + "." + assemblyVersion.Minor + "." + assemblyVersion.Build;
            }
        }
    }
}
