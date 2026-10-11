using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Verify.Tests
{
    public sealed class FixtureTests
    {
        public sealed class HashEntry { public string path { get; set; } public string transportTextSha256 { get; set; } }
        public sealed class Plan { public string status { get; set; } public bool isProjectGenerationSpec { get; set; } public List<Scenario> scenarios { get; set; } }
        public sealed class Scenario { public string id { get; set; } public string existingAp21Source { get; set; } public List<Artifact> objects { get; set; } public List<Table> tagTables { get; set; } }
        public sealed class Table { public string name { get; set; } public List<Tag> tags { get; set; } }
        public sealed class Tag { public string name { get; set; } public string dataType { get; set; } public string logicalAddress { get; set; } }
        public sealed class Artifact { public string id { get; set; } public string name { get; set; } public string kind { get; set; } public string sourcePath { get; set; } public List<string> dependsOn { get; set; } }
        private static string Data => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");

        [Fact]
        public void Fixture_bytes_are_bound_to_reviewable_transport_hashes()
        {
            var records = new JavaScriptSerializer().Deserialize<List<HashEntry>>(
                File.ReadAllText(Path.Combine(Data, "verification", "source-hashes.json")));
            Assert.Equal(6, records.Count);
            foreach (var record in records)
            {
                var file = Path.Combine(Data, record.path.Replace('/', Path.DirectorySeparatorChar));
                var text = ProjectGenerationVerifier.NormalizeTransportText(File.ReadAllText(file, new UTF8Encoding(false, true)));
                using (var sha = SHA256.Create())
                    Assert.Equal(record.transportTextSha256, BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant());
            }
        }

        [Fact]
        public void Plan_declares_the_whole_nonempty_program_and_stays_candidate()
        {
            var plan = new JavaScriptSerializer().Deserialize<Plan>(
                File.ReadAllText(Path.Combine(Data, "verification", "fixture-plan.json")));
            Assert.Equal("CANDIDATE", plan.status); Assert.False(plan.isProjectGenerationSpec);
            Assert.Equal(2, plan.scenarios.Count);
            foreach (var scenario in plan.scenarios)
            {
                Assert.Null(scenario.existingAp21Source);
                var ids = scenario.objects.Select(o => o.id).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
                foreach (var artifact in scenario.objects)
                {
                    Assert.All(artifact.dependsOn, dependency => Assert.Contains(dependency, ids));
                    var text = File.ReadAllText(Path.Combine(Data, artifact.sourcePath.Replace('/', Path.DirectorySeparatorChar)));
                    Assert.Contains("\"" + artifact.name + "\"", text);
                    Assert.False(string.IsNullOrWhiteSpace(text));
                }
            }
            var scl = plan.scenarios.Single(s => s.id == "scl-multiblock");
            Assert.Equal(new[] { "DB", "DB", "FB", "OB", "UDT" }, scl.objects.Select(o => o.kind).OrderBy(s => s).ToArray());
            var sd = File.ReadAllText(Path.Combine(Data, "programs", "lad-minimal", "LadContactCoil.s7dcl"));
            Assert.Contains("Contact( #Enable )", sd); Assert.Contains("Coil( #Active )", sd);
            Assert.DoesNotContain("S7_NetworkComment", sd); Assert.DoesNotContain("S7_BlockComment", sd);
        }
    }
}
