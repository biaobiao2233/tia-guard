using System;
using System.IO;
using TiaGuard.Openness;
#if PROJECT_GENERATION_SPEC_V1
using System.Collections.Generic;
#endif

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--self-test")
        {
            Console.Error.WriteLine("CANDIDATE: real V21 Smoke requires coordinator allocation, source identities and bound C compile service. Use --self-test for the offline harness check.");
            return 2;
        }
        var root = Path.Combine(Path.GetTempPath(), "tg45-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            // The absent compile adapter must stop execution before creating any TIA instance.
            var result = ProjectGenerationBuilder.Build(null, Path.Combine(root, "output"), root,
                (stage, plan) => throw new Exception("A TIA factory must never run during this self-test."), null);
            if (result.Published || result.Failures.Count == 0) throw new Exception("Unknown compiler was accepted.");
#if PROJECT_GENERATION_SPEC_V1
            var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Main.scl"), "ORGANIZATION_BLOCK \"Main\"\nBEGIN\nEND_ORGANIZATION_BLOCK\n");
            File.WriteAllText(Path.Combine(source, "project-generation.json"),
                "{\"schemaVersion\":\"1.0\",\"tiaVersion\":\"V21\",\"projectName\":\"Demo\",\"plc\":{\"name\":\"PLC_1\",\"cpuTypeIdentifier\":\"OrderNumber:6ES7 212-1AE40-0XB0/V4.7\"},\"tagTables\":[],\"artifacts\":[{\"id\":\"main\",\"kind\":\"OB\",\"name\":\"Main\",\"format\":\"scl\",\"sourcePath\":\"Main.scl\",\"dependsOn\":[]}]}");
            var input = ProjectGenerationSpecLoader.Load(Path.Combine(source, "project-generation.json"));
            var plan = ProjectGenerationBuilder.FromValidatedSpec(input,
                new Dictionary<string, ProjectGenerationObject> { ["main"] = new ProjectGenerationObject
                    { Kind = "OB", Name = "Main", Number = 1, Language = "SCL" } }, "SELF-TEST-FIXTURE");
            if (ProjectGenerationImporter.Plan(plan).Count != 1) throw new Exception("A/engine binding failed.");
            Console.WriteLine("PASS: offline A snapshot adapter; provider " + ProjectGenerationBuilder.BoundSpecRevision);
#endif
            Console.WriteLine("PASS: offline missing-compiler gate. No TIA instance/project/PLC was opened; this is not real Smoke evidence.");
#if PROJECT_GENERATION_COMPILATION_V1
            var unavailable = ProjectGenerationBuilder.FromCompilationReport(CompilationMessageCollector.Collect(null));
            if (unavailable.AllowsPublication) throw new Exception("C's unavailable report was accepted.");
            Console.WriteLine("PASS: unavailable C diagnostics stay failed; provider " + ProjectGenerationBuilder.BoundCompilationRevision);
#endif
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) FileSystemSafety.DeleteOwnedTree(root); }
    }
}
