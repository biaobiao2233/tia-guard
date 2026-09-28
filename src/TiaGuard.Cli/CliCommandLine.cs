using System;

namespace TiaGuard.Cli
{
    internal sealed class CliInvocation
    {
        internal string Command { get; set; }
        internal string Source { get; set; }
        internal string Target { get; set; }
    }

    internal static class CliCommandLine
    {
        internal static string Usage =>
            "Usage:" + Environment.NewLine +
            "  tia-guard doctor" + Environment.NewLine +
            "  tia-guard --version" + Environment.NewLine +
            "  tia-guard export <project.ap21> <repo-dir>" + Environment.NewLine +
            "  tia-guard build <repo-dir> --output <new-output-dir>" + Environment.NewLine +
            "  tia-guard verify <original.ap21> <rebuilt.ap21>";

        internal static bool TryParse(string[] args, out CliInvocation invocation)
        {
            invocation = null;
            if (args == null) return false;

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
