using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Verify.Tests
{
    public sealed class VerificationTests
    {
        [Theory]
        [InlineData(true, true, "PASS")]
        [InlineData(false, true, "UNKNOWN")]
        [InlineData(null, true, "UNKNOWN")]
        [InlineData(true, false, "UNKNOWN")]
        [InlineData(true, null, "UNKNOWN")]
        public void S71500RequiresSoftwareUnitAndRackReadback(bool? unitsEmpty, bool? rackOnlyCpu, string verdict)
        {
            var expected = Expected(); var actual = Actual();
            expected.CpuTypeIdentifier = actual.CpuTypeIdentifier = ProjectGenerationCpuProfiles.S71500CpuTypeIdentifier;
            actual.SoftwareUnitsEmpty = unitsEmpty; actual.NoExtraRackModules = rackOnlyCpu;
            actual.ReopenedFromDisk = true;
            Assert.Equal(verdict, ProjectGenerationVerifier.Compare(expected, actual).Verdict);
        }

        [Fact]
        public void S71500CannotPassUsing1200IdentityOrAReadFailure()
        {
            var expected = Expected(); var actual = Actual();
            expected.CpuTypeIdentifier = ProjectGenerationCpuProfiles.S71500CpuTypeIdentifier;
            actual.SoftwareUnitsEmpty = true; actual.NoExtraRackModules = true;
            actual.ReopenedFromDisk = true;
            Assert.Equal("FAIL", ProjectGenerationVerifier.Compare(expected, actual).Verdict);
            actual.CpuTypeIdentifier = expected.CpuTypeIdentifier;
            actual.ReadFailures.Add(new ProjectGenerationVerificationFinding { Code = "SOFTWARE_UNIT_READ_FAILED", Path = "plc", Detail = "synthetic unavailable enumeration" });
            Assert.Equal("UNKNOWN", ProjectGenerationVerifier.Compare(expected, actual).Verdict);
        }

        [Fact]
        public void S71500SameSessionReadbackCannotReplaceDiskReopen()
        {
            var expected = Expected(); var actual = Actual();
            expected.CpuTypeIdentifier = actual.CpuTypeIdentifier = ProjectGenerationCpuProfiles.S71500CpuTypeIdentifier;
            actual.SoftwareUnitsEmpty = true; actual.NoExtraRackModules = true;
            Assert.False(actual.ReopenedFromDisk);
            var result = ProjectGenerationVerifier.Compare(expected, actual);
            Assert.Equal("UNKNOWN", result.Verdict);
            Assert.Contains(result.Findings, f => f.Code == "REOPEN_UNPROVEN");
        }

        private static ProjectGenerationVerificationObject Object(string name = "Main") => new ProjectGenerationVerificationObject
        {
            Scope = "blocks", Name = name, Kind = "OB", Language = "SCL", Number = 1,
            ContentFormat = "scl", ContentComplete = true,
            Documents = new Dictionary<string, string> { [".scl"] = "BEGIN\n x := TRUE;\nEND_ORGANIZATION_BLOCK\n" },
            InterfaceComplete = true, InterfaceXml = "<Interface><Sections /></Interface>",
            InstanceComplete = true, InstanceOfName = ""
        };

        private static ProjectGenerationVerificationTagTable Table() => new ProjectGenerationVerificationTagTable
        {
            Name = "IO", Complete = true, Tags = new List<ProjectGenerationVerificationTag>
            { new ProjectGenerationVerificationTag { Name = "Start", DataType = "Bool", LogicalAddress = "%I0.0" } }
        };

        private static ProjectGenerationVerificationExpected Expected() => new ProjectGenerationVerificationExpected
        {
            ProjectName = "Fresh", PlcName = "PLC_1", CpuTypeIdentifier = "OrderNumber:6ES7 212-1AE40-0XB0/V4.7",
            EvidenceReference = "synthetic-test-vector-v1 (not a V21 receipt)",
            Objects = new List<ProjectGenerationVerificationObject> { Object() },
            TagTables = new List<ProjectGenerationVerificationTagTable> { Table() }
        };

        private static ProjectGenerationVerificationObservation Actual() => new ProjectGenerationVerificationObservation
        {
            ProjectName = "Fresh", PlcName = "PLC_1", CpuTypeIdentifier = "OrderNumber:6ES7 212-1AE40-0XB0/V4.7",
            TiaVersion = "V21", ApiVersion = "21.0.0.0", HardwareComplete = true, ObjectsComplete = true, TagTablesComplete = true,
            Objects = new List<ProjectGenerationVerificationObject> { Object() },
            TagTables = new List<ProjectGenerationVerificationTagTable> { Table() }
        };

        [Fact]
        public void Exact_complete_observation_passes_comparison_only()
        {
            var result = ProjectGenerationVerifier.Compare(Expected(), Actual());
            Assert.Equal("PASS", result.Verdict);
            Assert.Empty(result.Findings);
            Assert.Contains(result.Compared, p => p.EndsWith(":SOURCE_TEXT"));
        }

        [Theory]
        [InlineData("body")]
        [InlineData("interface")]
        [InlineData("instance")]
        [InlineData("missing")]
        [InlineData("extra")]
        [InlineData("scope")]
        [InlineData("kind")]
        [InlineData("language")]
        [InlineData("number")]
        [InlineData("cpu")]
        [InlineData("plc")]
        [InlineData("project")]
        [InlineData("tag-type")]
        [InlineData("tag-address")]
        [InlineData("tag-extra")]
        [InlineData("tag-missing")]
        [InlineData("table-extra")]
        [InlineData("table-missing")]
        [InlineData("resource-extra")]
        [InlineData("duplicate-object")]
        [InlineData("duplicate-tag")]
        [InlineData("duplicate-table")]
        [InlineData("unknown-xml")]
        public void Semantic_or_inventory_mutations_cannot_pass(string mutation)
        {
            var a = Actual();
            var o = a.Objects[0];
            switch (mutation)
            {
                case "body": o.Documents[".scl"] = o.Documents[".scl"].Replace("TRUE", "FALSE"); break;
                case "interface": o.InterfaceXml = "<Interface><Unknown /></Interface>"; break;
                case "instance": o.InstanceOfName = "OtherFB"; break;
                case "missing": a.Objects.Clear(); break;
                case "extra": a.Objects.Add(Object("Extra")); break;
                case "scope": o.Scope = "blocks/Nested"; break;
                case "kind": o.Kind = "FB"; break;
                case "language": o.Language = "LAD"; break;
                case "number": o.Number = 2; break;
                case "cpu": a.CpuTypeIdentifier = a.CpuTypeIdentifier.Replace("V4.7", "V4.6"); break;
                case "plc": a.PlcName = "Other"; break;
                case "project": a.ProjectName = "Other"; break;
                case "tag-type": a.TagTables[0].Tags[0].DataType = "Byte"; break;
                case "tag-address": a.TagTables[0].Tags[0].LogicalAddress = "%Q0.0"; break;
                case "tag-extra": a.TagTables[0].Tags.Add(new ProjectGenerationVerificationTag { Name = "Extra" }); break;
                case "tag-missing": a.TagTables[0].Tags.Clear(); break;
                case "table-extra": a.TagTables.Add(new ProjectGenerationVerificationTagTable { Name = "Extra", Complete = true }); break;
                case "table-missing": a.TagTables.Clear(); break;
                case "resource-extra": o.Documents.Add(".s7res", "unexpected multilingual resource"); break;
                case "duplicate-object": a.Objects.Add(Object()); break;
                case "duplicate-tag": a.TagTables[0].Tags.Add(a.TagTables[0].Tags[0]); break;
                case "duplicate-table": a.TagTables.Add(Table()); break;
                case "unknown-xml": o.Documents[".scl"] += "<UnknownExtension meaning='different'/>"; break;
            }
            Assert.Equal("FAIL", ProjectGenerationVerifier.Compare(Expected(), a).Verdict);
        }

        [Theory]
        [InlineData("hardware")]
        [InlineData("objects")]
        [InlineData("tags")]
        [InlineData("content")]
        [InlineData("interface")]
        [InlineData("instance")]
        [InlineData("number")]
        [InlineData("cpu")]
        [InlineData("api")]
        [InlineData("collection")]
        [InlineData("null-entry")]
        [InlineData("failure")]
        [InlineData("format")]
        [InlineData("empty-doc")]
        [InlineData("blank-doc")]
        public void Incomplete_or_unproven_reads_are_unknown(string dimension)
        {
            var a = Actual();
            switch (dimension)
            {
                case "hardware": a.HardwareComplete = false; break;
                case "objects": a.ObjectsComplete = false; break;
                case "tags": a.TagTablesComplete = false; break;
                case "content": a.Objects[0].ContentComplete = false; break;
                case "interface": a.Objects[0].InterfaceComplete = false; break;
                case "instance": a.Objects[0].InstanceOfName = null; break;
                case "number": a.Objects[0].Number = null; break;
                case "cpu": a.CpuTypeIdentifier = null; break;
                case "api": a.ApiVersion = null; break;
                case "collection": a.ReadFailures = null; break;
                case "null-entry": a.Objects.Add(null); break;
                case "failure": a.ReadFailures.Add(new ProjectGenerationVerificationFinding { Code = "MoveNextFailed", Path = "blocks" }); break;
                case "format": a.Objects[0].ContentFormat = "simatic-sd"; break;
                case "empty-doc": a.Objects[0].Documents[".scl"] = ""; break;
                case "blank-doc": a.Objects[0].Documents[".scl"] = "\uFEFF \r\n"; break;
            }
            Assert.Equal("UNKNOWN", ProjectGenerationVerifier.Compare(Expected(), a).Verdict);
        }

        [Fact]
        public void Expected_side_must_also_be_complete_and_have_provenance()
        {
            var e = Expected();
            e.EvidenceReference = null;
            e.Objects[0].InterfaceComplete = false;
            Assert.Equal("UNKNOWN", ProjectGenerationVerifier.Compare(e, Actual()).Verdict);
            Assert.Equal("UNKNOWN", ProjectGenerationVerifier.Compare(null, Actual()).Verdict);
            Assert.Equal("UNKNOWN", ProjectGenerationVerifier.Compare(e, null).Verdict);
        }

        [Fact]
        public void Only_BOM_and_transport_line_endings_are_normalized()
        {
            var a = Actual();
            a.Objects[0].Documents[".scl"] = "\uFEFF" + a.Objects[0].Documents[".scl"].Replace("\n", "\r\n");
            Assert.Equal("PASS", ProjectGenerationVerifier.Compare(Expected(), a).Verdict);
            a.Objects[0].Documents[".scl"] += " ";
            Assert.Equal("FAIL", ProjectGenerationVerifier.Compare(Expected(), a).Verdict);
        }

        [Fact]
        public void Inventory_order_has_no_meaning_but_identity_scope_does()
        {
            var e = Expected(); var a = Actual();
            e.Objects.Add(Object("Other")); a.Objects.Insert(0, Object("Other"));
            Assert.Equal("PASS", ProjectGenerationVerifier.Compare(e, a).Verdict);
            e.Objects[0].Scope = "blocks|5:Other";
            Assert.Equal("FAIL", ProjectGenerationVerifier.Compare(e, a).Verdict);
        }

        [Fact]
        public void Empty_program_or_unsupported_API_cannot_be_accepted()
        {
            var e = Expected(); var a = Actual();
            e.Objects.Clear(); a.Objects.Clear();
            Assert.Equal("UNKNOWN", ProjectGenerationVerifier.Compare(e, a).Verdict);
            e = Expected(); a = Actual(); a.ApiVersion = "20.0.0.0";
            Assert.Equal("UNKNOWN", ProjectGenerationVerifier.Compare(e, a).Verdict);
            a.ApiVersion = "future";
            Assert.Equal("UNKNOWN", ProjectGenerationVerifier.Compare(e, a).Verdict);
            e = Expected(); a = Actual(); e.CpuTypeIdentifier = a.CpuTypeIdentifier = "other-cpu";
            Assert.Equal("UNKNOWN", ProjectGenerationVerifier.Compare(e, a).Verdict);
        }

        [Fact]
        public void Multi_object_and_instance_positive_case_compares_every_declared_object()
        {
            var e = Expected(); var a = Actual();
            foreach (var list in new[] { e.Objects, a.Objects })
            {
                var fb = Object("Latch"); fb.Kind = "FB"; fb.Number = 2;
                var db = Object("LatchInstance"); db.Kind = "DB"; db.Language = "DB"; db.Number = 3; db.InstanceOfName = "Latch";
                var udt = Object("MotorStatus"); udt.Kind = "UDT"; udt.Language = "UDT"; udt.Scope = "types"; udt.Number = null;
                list.Add(fb); list.Add(db); list.Add(udt);
            }
            Assert.Equal("PASS", ProjectGenerationVerifier.Compare(e, a).Verdict);
            a.Objects.Single(o => o.Name == "LatchInstance").InstanceOfName = "WrongTarget";
            Assert.Equal("FAIL", ProjectGenerationVerifier.Compare(e, a).Verdict);
        }

        [Theory]
        [InlineData("SW.Blocks.OB", "OB", "")]
        [InlineData("SW.Blocks.InstanceDB", "DB", "Latch")]
        [InlineData("SW.Types.PlcStruct", "UDT", "")]
        public void Xml_interface_and_instance_are_read_from_actual_export(string kind, string expectedKind, string instance)
        {
            var o = Object(); o.Kind = expectedKind;
            var metadata = instance == "" ? "" : "<InstanceOfName>" + instance + "</InstanceOfName>";
            var xml = "<Document><" + kind + "><AttributeList><Name>Main</Name><Interface><Sections><Unknown attr='keep'/></Sections></Interface>"
                + metadata + "</AttributeList></" + kind + "></Document>";
            ProjectGenerationVerificationXml.ReadInterface(xml, o);
            Assert.True(o.InterfaceComplete); Assert.True(o.InstanceComplete);
            Assert.Contains("Unknown", o.InterfaceXml); Assert.Equal(instance, o.InstanceOfName);
        }

        [Theory]
        [InlineData("<!DOCTYPE Document [<!ENTITY x SYSTEM 'file:///private'>]><Document>&x;</Document>")]
        [InlineData("<Document><SW.Blocks.OB><AttributeList><Name>Wrong</Name><Interface /></AttributeList></SW.Blocks.OB></Document>")]
        [InlineData("<Document><SW.Blocks.OB><AttributeList><Name>Main</Name></AttributeList></SW.Blocks.OB></Document>")]
        [InlineData("<Document><SW.Blocks.Future /></Document>")]
        public void Unknown_ambiguous_or_unsafe_xml_does_not_manufacture_interface(string xml)
        {
            var o = Object();
            Assert.ThrowsAny<Exception>(() => ProjectGenerationVerificationXml.ReadInterface(xml, o));
            Assert.False(o.InterfaceComplete); Assert.False(o.InstanceComplete);
        }

        [Fact]
        public void InstanceDB_without_instance_metadata_stays_unknown()
        {
            var o = Object(); o.Kind = "DB";
            Assert.ThrowsAny<Exception>(() => ProjectGenerationVerificationXml.ReadInterface(
                "<Document><SW.Blocks.InstanceDB><AttributeList><Name>Main</Name><Interface /></AttributeList></SW.Blocks.InstanceDB></Document>", o));
            Assert.False(o.InstanceComplete);
        }

        [Theory]
        [InlineData("success")]
        [InlineData("extra")]
        [InlineData("missing")]
        [InlineData("escape")]
        [InlineData("empty")]
        [InlineData("bad-utf8")]
        [InlineData("unknown-extension")]
        [InlineData("duplicate")]
        public void Export_file_ledger_checks_actual_files_and_strict_utf8(string scenario)
        {
            var root = Path.Combine(Path.GetTempPath(), "tia-guard-47-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var file = Path.Combine(root, "object.s7dcl");
                File.WriteAllText(file, "FUNCTION_BLOCK \"Candidate\"\nEND_FUNCTION_BLOCK", new UTF8Encoding(true));
                var report = new List<string> { file };
                if (scenario == "extra") File.WriteAllText(Path.Combine(root, "object.s7res"), "resource");
                if (scenario == "missing") report.Add(Path.Combine(root, "missing.s7res"));
                if (scenario == "escape") report.Add(Path.Combine(root, "..", "other.s7dcl"));
                if (scenario == "empty") File.WriteAllText(file, "");
                if (scenario == "bad-utf8") File.WriteAllBytes(file, new byte[] { 0xC3, 0x28 });
                if (scenario == "unknown-extension") { var extra = Path.Combine(root, "other.foo"); File.WriteAllText(extra, "unknown"); report.Add(extra); }
                if (scenario == "duplicate") report.Add(file);
                var o = Object(); o.ContentFormat = "simatic-sd";
                if (scenario == "success")
                {
                    ProjectGenerationReadbackDocuments.Read(root, report, o);
                    Assert.True(o.ContentComplete); Assert.Single(o.Documents);
                    Assert.Equal(64, o.RawSha256[".s7dcl"].Length);
                }
                else
                {
                    Assert.ThrowsAny<Exception>(() => ProjectGenerationReadbackDocuments.Read(root, report, o));
                    Assert.False(o.ContentComplete);
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
