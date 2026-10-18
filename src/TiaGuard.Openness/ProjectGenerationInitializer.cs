using System;
using System.IO;
using System.Text;

namespace TiaGuard.Openness
{
    public static class ProjectGenerationInitializer
    {
        public const string TemplateIdentity = "tia-guard/project-generation-v1/minimal-scl-main@1";
        // A self-contained, editable input; not an existing-project export or compile receipt.
        private const string SpecText = "{\n  \"schemaVersion\": \"1.0\",\n  \"tiaVersion\": \"V21\",\n  \"projectName\": \"NewProject\",\n  \"plc\": {\"name\": \"PLC_1\", \"cpuTypeIdentifier\": \"OrderNumber:6ES7 212-1AE40-0XB0/V4.7\"},\n  \"tagTables\": [],\n  \"artifacts\": [{\"id\": \"main\", \"kind\": \"OB\", \"name\": \"Main\", \"format\": \"scl\", \"sourcePath\": \"sources/Main.scl\", \"dependsOn\": []}]\n}\n";
        private const string MainText = "// Template: " + TemplateIdentity + "\n// Offline input only; V21 compilation and generated-object verification are required.\nORGANIZATION_BLOCK \"Main\"\n{ S7_Optimized_Access := 'TRUE' }\nVERSION : 0.1\nBEGIN\n    // Add program logic here.\nEND_ORGANIZATION_BLOCK\n";
        public static ProjectGenerationSpecInput Initialize(string newDirectory)
            => Initialize(newDirectory, ProjectGenerationCpuProfiles.DefaultProfileId);
        public static ProjectGenerationSpecInput Initialize(string newDirectory, string cpuProfileId)
        {
            var profile = ProjectGenerationCpuProfiles.RequireById(cpuProfileId);
            if (string.IsNullOrWhiteSpace(newDirectory)) throw new ArgumentException("A new directory is required.");
            var output = Path.GetFullPath(newDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(output);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) throw new DirectoryNotFoundException("The output parent must exist.");
            FileSystemSafety.RequirePlainAncestors(parent);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Init output already exists; no overwrite is allowed.");
            var stage = Path.Combine(parent, ".tia-guard-init-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(stage) || File.Exists(stage)) throw new IOException("Init staging path already exists.");
            Directory.CreateDirectory(stage);
            try
            {
                Directory.CreateDirectory(Path.Combine(stage, "sources"));
                WriteNew(Path.Combine(stage, ProjectGenerationSpecLoader.DefaultSpecFileName),
                    SpecText.Replace(ProjectGenerationCpuProfiles.DefaultCpuTypeIdentifier, profile.CpuTypeIdentifier));
                WriteNew(Path.Combine(stage, "sources", "Main.scl"), MainText);
                ProjectGenerationSpecLoader.Load(Path.Combine(stage, ProjectGenerationSpecLoader.DefaultSpecFileName));
                FileSystemSafety.RequirePlainAncestors(parent);
                Directory.Move(stage, output); // Atomic same-parent publication; refuses a raced destination.
                return ProjectGenerationSpecLoader.Load(Path.Combine(output, ProjectGenerationSpecLoader.DefaultSpecFileName));
            }
            finally { if (Directory.Exists(stage)) FileSystemSafety.DeleteOwnedTree(stage); }
        }
        private static void WriteNew(string path, string text)
        {
            var bytes = new UTF8Encoding(false).GetBytes(text);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes, 0, bytes.Length);
        }
    }
}
