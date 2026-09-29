using System;
using System.IO;

namespace TiaGuard.Openness
{
    internal sealed class RoundTripBuildLayout
    {
        internal const int TiaV21MaximumProjectDirectoryLength = 143;

        internal string WorkingRoot { get; private set; }
        internal string StageDirectory { get; private set; }
        internal string StagedProjectDirectory { get; private set; }

        internal static string DefaultWorkingRoot()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) local = Path.GetTempPath();
            return Path.Combine(local, "TG", "w");
        }

        internal static RoundTripBuildLayout Plan(string projectName, string token = null)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("A TIA project name is required.", nameof(projectName));
            var root = Path.GetFullPath(DefaultWorkingRoot()).TrimEnd(Path.DirectorySeparatorChar);
            var id = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token;
            if (!Guid.TryParseExact(id, "N", out _))
                throw new ArgumentException("The build staging token is invalid.", nameof(token));

            var stage = Path.Combine(root, "b-" + id);
            var project = Path.Combine(stage, projectName);
            if (project.Length > TiaV21MaximumProjectDirectoryLength)
                throw new PathTooLongException(
                    "The TIA project name is too long for the bounded V21 staging path.");

            return new RoundTripBuildLayout
            {
                WorkingRoot = root,
                StageDirectory = stage,
                StagedProjectDirectory = project
            };
        }
    }
}
