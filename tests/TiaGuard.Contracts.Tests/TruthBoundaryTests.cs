using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class TruthBoundaryTests
    {
        private static void AssertBlocked(CanonicalFixture f)
        {
            Assert.ThrowsAny<Exception>(() => RoundTripBuildInput.LoadSource(f.Root));
            Assert.Equal("blocked", RoundTripVerifier.CompareSources(f.Root, f.Root).Verdict);
        }

        [Fact]
        public void ValidSourceAndReadyExportPassTheSameGate()
        {
            using (var f = new CanonicalFixture())
            {
                Assert.Equal("pass", RoundTripVerifier.CompareSources(f.Root, f.Root).Verdict);
                Assert.True(f.Export("export").RoundTripReady);
                RoundTripBuildInput.LoadSource(Path.Combine(f.OwnedRoot, "export"));
            }
        }

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData("%DB1.DBX0.0")] [InlineData("%IW0")]
        public void InvalidBoolAddressBlocks(string address)
        {
            using (var f = new CanonicalFixture()) { f.Tag.Address = address; f.Save(); AssertBlocked(f); }
        }

        [Theory]
        [InlineData("OrderNumber:6ES7 515-2UM01-0AB0/V2.9")]
        [InlineData("OrderNumber:6ES7 212-1AE40-0XB0/V4.6")]
        [InlineData("OrderNumber:")]
        public void UnprovenCpuIsNotSupported(string identifier)
        {
            using (var f = new CanonicalFixture())
            {
                f.Hardware.CreateTypeIdentifier = identifier; f.Save(); AssertBlocked(f);
                Assert.False(f.Export("export").RoundTripReady);
            }
        }

        [Theory]
        [InlineData("Created")] [InlineData("Name")] [InlineData("Number")] [InlineData("ProgrammingLanguage")]
        public void DuplicateXmlIdentityBlocks(string element)
        {
            using (var f = new CanonicalFixture())
            {
                f.Xml = f.Xml.Replace("</" + element + ">", "</" + element + "><" + element + ">other</" + element + ">");
                f.Save(); AssertBlocked(f);
            }
        }

        [Theory]
        [InlineData("<SW.Blocks.FC />")] [InlineData("<SW.Blocks.FB />")]
        [InlineData("<SW.Blocks.OB />")] [InlineData("<x:SW.Blocks.FC xmlns:x='extra' />")]
        [InlineData("<DocumentInfo><Created>other</Created></DocumentInfo>")]
        public void ExtraBlockOrDocumentInfoBlocks(string extra)
        {
            using (var f = new CanonicalFixture()) { f.Xml = f.Xml.Replace("</Document>", extra + "</Document>"); f.Save(); AssertBlocked(f); }
        }

        [Fact]
        public void UnknownXmlRemainsComparisonSignificant()
        {
            using (var a = new CanonicalFixture())
            using (var b = new CanonicalFixture())
            {
                b.Xml = b.Xml.Replace("</Document>", "<Unknown>engineering-content</Unknown></Document>"); b.Save();
                var diff = RoundTripVerifier.CompareSources(a.Root, b.Root);
                Assert.Equal("mismatch", diff.Verdict);
                Assert.Contains(diff.Differences, d => d.Field == "source.sha256");
            }
        }

        [Theory]
        [InlineData("\"name\":\"Demo\"", "\"name\":123")]
        [InlineData("\"roundTripReady\":true", "\"roundTripReady\":\"true\"")]
        [InlineData("\"roundTripReady\":true", "\"roundTripReady\":false,\"roundTripReady\":true")]
        public void CoercibleOrDuplicateJsonIsRejected(string original, string changed)
        {
            using (var f = new CanonicalFixture())
            {
                var file = f.PathOf("tia-guard.json");
                Assert.Contains(original, File.ReadAllText(file));
                File.WriteAllText(file, File.ReadAllText(file).Replace(original, changed)); AssertBlocked(f);
            }
        }

        [Fact]
        public void OriginalProjectIdentityIsAcceptedWhenComplete()
        {
            using (var f = new CanonicalFixture())
            {
                f.Manifest.Project.OriginalFileName = "Demo.ap21";
                f.Manifest.Project.OriginalSizeBytes = 12345;
                f.Manifest.Project.OriginalSha256 =
                    "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
                f.Save();

                var input = RoundTripBuildInput.LoadSource(f.Root);
                Assert.Equal("Demo.ap21", input.Manifest.Project.OriginalFileName);
                Assert.Equal(12345, input.Manifest.Project.OriginalSizeBytes);
                Assert.Equal(
                    "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    input.Manifest.Project.OriginalSha256);
            }
        }

        [Fact]
        public void IncompleteOriginalProjectIdentityIsRejected()
        {
            using (var f = new CanonicalFixture())
            {
                f.Manifest.Project.OriginalFileName = "Demo.ap21";
                f.Save();
                AssertBlocked(f);
            }
        }

        [Fact]
        public void UnsafeOriginalProjectFileNameIsRejected()
        {
            using (var f = new CanonicalFixture())
            {
                f.Manifest.Project.OriginalFileName = "../Demo.ap21";
                f.Manifest.Project.OriginalSizeBytes = 12345;
                f.Manifest.Project.OriginalSha256 =
                    "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
                f.Save();
                AssertBlocked(f);
            }
        }

        [Theory]
        [InlineData("../outside.json")] [InlineData("C:/outside.json")] [InlineData("tia\\hardware\\station-a.json")]
        [InlineData("tia/hardware/CON.json")] [InlineData("tia/hardware/station-a.json.")]
        public void UnsafePathsAreRejected(string path)
        {
            using (var f = new CanonicalFixture()) { f.Manifest.Hardware[0] = path; f.Save(); AssertBlocked(f); }
        }

        [Theory]
        [InlineData("table")] [InlineData("block")]
        public void NestedScopeCannotBeReady(string kind)
        {
            using (var f = new CanonicalFixture())
            {
                var manifest = f.Export("nested", (snapshot, hints) =>
                {
                    if (kind == "table")
                    {
                        hints.TagTables[0].ScopePath = "Station/PLC/PLC/PLC%20tags/Nested/Default";
                        snapshot.Plcs[0].Tags[0].ScopePath = hints.TagTables[0].ScopePath;
                    }
                    else snapshot.Plcs[0].Blocks[0].ScopePath += "/Nested";
                });
                Assert.False(manifest.RoundTripReady);
                Assert.Contains(manifest.Diagnostics, d => d.Code == "CANONICAL_SOURCE_INVALID");
                Assert.Equal("blocked", RoundTripVerifier.CompareSources(Path.Combine(f.OwnedRoot, "nested"), f.Root).Verdict);
            }
        }

        [Theory]
        [InlineData("USER_CONSTANTS")] [InlineData("PLC_TYPES")] [InlineData("EXTERNAL_SOURCES")]
        [InlineData("TECHNOLOGY_OBJECTS")] [InlineData("WATCH_TABLES")]
        [InlineData("FORCE_TABLES")] [InlineData("USER_ALARM_TEXTLISTS")]
        public void UnsupportedInventoryBlocksEvenWhenCaptureClaimsComplete(string category)
        {
            using (var f = new CanonicalFixture())
            {
                var manifest = f.Export("unsupported", (snapshot, hints) =>
                    UnsupportedInventory.Check(() => new[] { "extra" }, snapshot, category, "plc:example"));
                Assert.False(manifest.RoundTripReady);
                Assert.Contains(manifest.Diagnostics, d => d.Code == category + "_UNSUPPORTED");
            }
        }

        [Theory]
        [InlineData("null")] [InlineData("throw")] [InlineData("enumerator")]
        public void UnreadableInventoryIsNotEmpty(string mode)
        {
            var snapshot = new SnapshotV1();
            UnsupportedInventory.Check<string>(() => mode == "null" ? null : mode == "throw" ?
                throw new IOException("private-path") : FailingEnumeration(), snapshot, "USER_CONSTANTS", "plc:example");
            Assert.Equal("USER_CONSTANTS_SCAN_FAILED", Assert.Single(snapshot.Diagnostics).Code);
            Assert.DoesNotContain("private-path", SnapshotV1Json.Serialize(snapshot));
        }

        private static IEnumerable<string> FailingEnumeration()
        {
            throw new IOException("private-path");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        [Fact]
        public void ObservedEmptyInventoryIsAccepted()
        {
            var snapshot = new SnapshotV1();
            UnsupportedInventory.Check(() => new string[0], snapshot, "USER_CONSTANTS", "plc:example");
            Assert.Empty(snapshot.Diagnostics);
        }

        [Theory]
        [InlineData("Force table", 0, true)]
        [InlineData("Force table", 1, false)]
        [InlineData("User force table", 0, false)]
        [InlineData(null, 0, false)]
        public void OnlyTheObservedEmptyDefaultForceTableIsImplicit(string name, int entries, bool allowed)
        {
            Assert.Equal(allowed, RoundTripProfile.IsDefaultForceTable(name, entries));
        }

        [Fact]
        public void ImportUsesFrozenBytesAndHoldsAReadOnlyShare()
        {
            using (var f = new CanonicalFixture())
            {
                var input = RoundTripBuildInput.LoadSource(f.Root);
                File.WriteAllText(f.PathOf(CanonicalFixture.XmlPath), "changed after validation");
                using (var frozen = input.OpenValidatedBlockSource(f.OwnedRoot))
                {
                    Assert.Equal(f.Xml, File.ReadAllText(frozen.Name));
                    Assert.Throws<IOException>(() => File.WriteAllText(frozen.Name, "replace"));
                    Assert.Throws<IOException>(() => File.Delete(frozen.Name));
                }
            }
        }

        [Theory]
        [InlineData("hash")] [InlineData("extra")] [InlineData("capability")] [InlineData("dtd")]
        public void ExistingFailClosedGuardsRemain(string change)
        {
            using (var f = new CanonicalFixture())
            {
                if (change == "hash") File.AppendAllText(f.PathOf(CanonicalFixture.XmlPath), " ");
                if (change == "extra") File.WriteAllText(f.PathOf("extra.txt"), "extra");
                if (change == "capability") { f.Manifest.Capabilities[0].State = "unsupported"; f.Save(); }
                if (change == "dtd") { f.Xml = "<!DOCTYPE Document [<!ENTITY x 'Main'>]>" + f.Xml; f.Save(); }
                AssertBlocked(f);
            }
        }
    }
}
