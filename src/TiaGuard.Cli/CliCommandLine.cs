using System;

namespace TiaGuard.Cli
{
    internal sealed class CliInvocation
    {
        internal string Command { get; set; }
        internal string Source { get; set; }
        internal string Target { get; set; }
        internal string Scratch { get; set; }
        internal string PublicApi { get; set; }
    }

    internal static class CliCommandLine
    {
        internal static string Usage =>
            "Usage:" + Environment.NewLine +
            "  tia-guard doctor" + Environment.NewLine +
            "  tia-guard --version" + Environment.NewLine +
            "  tia-guard init <new-directory>" + Environment.NewLine +
            "  tia-guard ai-view <tia-source-dir>" + Environment.NewLine +
            "  tia-guard export <project.ap21> <repo-dir>" + Environment.NewLine +
            "  tia-guard build <repo-dir> --output <new-output-dir>" + Environment.NewLine +
            "  tia-guard build-generation <source-dir> --output <new-output-dir> --scratch <owned-directory> [--api <V21-api-dir>]" + Environment.NewLine +
            "  tia-guard verify <original.ap21> <rebuilt.ap21>";

        internal static bool TryParse(string[] args, out CliInvocation invocation)
        {
            invocation = null;
            if (args == null) return false;
            if ((args.Length == 6 || args.Length == 8) && args[0] == "build-generation" &&
                args[2] == "--output" && args[4] == "--scratch" && Present(args[1]) && Present(args[3]) && Present(args[5]) &&
                (args.Length == 6 || args[6] == "--api" && Present(args[7])))
            {
                invocation = new CliInvocation { Command = "build-generation", Source = args[1], Target = args[3],
                    Scratch = args[5], PublicApi = args.Length == 8 ? args[7] : null };
                return true;
            }

            if (args.Length == 2 && args[0] == "init" && Present(args[1]))
            {
                invocation = new CliInvocation { Command = "init", Target = args[1] };
                return true;
            }

            if (args.Length == 2 && args[0] == "ai-view" && Present(args[1]))
            {
                invocation = new CliInvocation { Command = "ai-view", Source = args[1] };
                return true;
            }

            if (args.Length == 1 && args[0] == "doctor")
            {
                invocation = new CliInvocation
                {
                    Command = "doctor"
                };
                return true;
            }

            if (args.Length == 3 && args[0] == "export" &&
                Present(args[1]) && Present(args[2]))
            {
                invocation = new CliInvocation
                {
                    Command = "export",
                    Source = args[1],
                    Target = args[2]
                };
                return true;
            }

            if (args.Length == 4 && args[0] == "build" && args[2] == "--output" &&
                Present(args[1]) && Present(args[3]))
            {
                invocation = new CliInvocation
                {
                    Command = "build",
                    Source = args[1],
                    Target = args[3]
                };
                return true;
            }

            if (args.Length == 3 && args[0] == "verify" &&
                Present(args[1]) && Present(args[2]))
            {
                invocation = new CliInvocation
                {
                    Command = "verify",
                    Source = args[1],
                    Target = args[2]
                };
                return true;
            }

            return false;
        }

        private static bool Present(string value) => !string.IsNullOrWhiteSpace(value);
    }
}
