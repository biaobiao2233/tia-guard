using System;
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

            if (!CliCommandLine.TryParse(args, out var invocation))
            {
                Console.Error.WriteLine(CliCommandLine.Usage);
                return 2;
            }

            try
            {
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
    }
}
