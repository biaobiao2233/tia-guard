using System;
using TiaGuard.Openness;

namespace TiaGuard.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 4 || args[0] != "build" || args[2] != "--output" ||
                string.IsNullOrWhiteSpace(args[1]) || string.IsNullOrWhiteSpace(args[3]))
            {
                Console.Error.WriteLine("Usage: tia-guard build <repo-dir> --output <new-output-dir>");
                return 2;
            }

            try
            {
                var result = RoundTripBuilder.Build(args[1], args[3],
                    stage => Console.Error.WriteLine("stage=" + stage));
                Console.WriteLine("projectFile=" + result.ProjectFile);
                Console.WriteLine("compileErrors=" + result.CompileErrors);
                Console.WriteLine("compileWarnings=" + result.CompileWarnings);
                return 0;
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
