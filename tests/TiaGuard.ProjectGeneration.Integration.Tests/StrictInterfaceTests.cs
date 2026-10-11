using System;
using System.Collections.Generic;
using System.IO;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Integration.Tests
{
    public sealed class StrictInterfaceTests
    {
        private const string Flags = "<AttributeList><BooleanAttribute Name='ExternalAccessible' SystemDefined='true'>true</BooleanAttribute><BooleanAttribute Name='ExternalVisible' SystemDefined='true'>true</BooleanAttribute><BooleanAttribute Name='ExternalWritable' SystemDefined='true'>true</BooleanAttribute><BooleanAttribute Name='SetPoint' SystemDefined='true'>false</BooleanAttribute></AttributeList>";
        private static string InstanceXml => "<Interface><Sections><Section Name='Static'><Member Name='State' Datatype='Bool' Remanence='NonRetain' Accessibility='Public'>" + Flags + "</Member></Section></Sections></Interface>";

        [Fact]
        public void InstanceStorageDefaultsAreQualifiedIndependentlyOfItsReferenceOnlySource()
        {
            var source = ProjectGenerationSclProjection.Parse("FUNCTION_BLOCK \"Controller\"\nVERSION : 0.1\nVAR\nState : Bool;\nEND_VAR\nBEGIN\nEND_FUNCTION_BLOCK")[0];
            Assert.Equal(source.Interface, ProjectGenerationSclProjection.XmlInterface(InstanceXml, "DB", true));
        }

        [Theory]
        [InlineData("NonRetain", "Retain")]
        [InlineData("Accessibility='Public'", "Accessibility='Private'")]
        [InlineData("Name='ExternalAccessible' SystemDefined='true'>true", "Name='ExternalAccessible' SystemDefined='true'>false")]
        [InlineData("Name='SetPoint' SystemDefined='true'>false", "Name='SetPoint' SystemDefined='true'>true")]
        [InlineData("Remanence='NonRetain'", "")]
        [InlineData("Datatype='Bool'", "Datatype='Bool' Mystery='1'")]
        [InlineData("</Member>", "<StartValue>TRUE</StartValue></Member>")]
        [InlineData("</Member>", "<Unknown><Member Name='Hidden' Datatype='Bool'/></Unknown></Member>")]
        public void StorageMetadataAndUnknownShapesCannotBecomeAFalsePass(string oldValue, string replacement)
        {
            Assert.Throws<InvalidDataException>(() => ProjectGenerationSclProjection.XmlInterface(InstanceXml.Replace(oldValue, replacement), "DB", true));
        }

        [Fact]
        public void NamedUdtExpansionComesFromSourceAndEveryNestedMemberIsCompared()
        {
            var udt = ProjectGenerationSclProjection.Parse("TYPE \"StateType\"\nVERSION : 0.1\nSTRUCT\nReady : Bool;\nEND_STRUCT;\nEND_TYPE")[0];
            var db = ProjectGenerationSclProjection.Parse("DATA_BLOCK \"Status\"\nVERSION : 0.1\nVAR\nState : \"StateType\";\nEND_VAR\nBEGIN\nEND_DATA_BLOCK")[0];
            var expected = ProjectGenerationSclProjection.ExpandTypes(db.Interface, new Dictionary<string, string> { [udt.Name] = udt.Interface });
            var xml = InstanceXml.Replace("Datatype='Bool'", "Datatype='&quot;StateType&quot;'").Replace("SetPoint' SystemDefined='true'>false", "SetPoint' SystemDefined='true'>true").Replace("</Member>", "<Sections><Section Name='None'><Member Name='Ready' Datatype='Bool'/></Section></Sections></Member>");
            Assert.Equal(expected, ProjectGenerationSclProjection.XmlInterface(xml, "DB"));
            Assert.NotEqual(expected, ProjectGenerationSclProjection.XmlInterface(xml.Replace("Name='Ready' Datatype='Bool'", "Name='Ready' Datatype='Int'"), "DB"));
            Assert.NotEqual(expected, ProjectGenerationSclProjection.XmlInterface(xml.Replace("<Member Name='Ready' Datatype='Bool'/>", ""), "DB"));
            Assert.Throws<InvalidDataException>(() => ProjectGenerationSclProjection.ExpandTypes(db.Interface, new Dictionary<string, string>()));
        }

        [Theory]
        [InlineData("Int")]
        [InlineData("Void")]
        public void FcReturnUsesRetValAndStableSectionOrder(string type)
        {
            var source = ProjectGenerationSclProjection.Parse("FUNCTION \"Calculate\" : " + type + "\nVERSION : 0.1\nVAR_INPUT\nX : Int;\nEND_VAR\nBEGIN\nEND_FUNCTION")[0];
            var input = "<Section Name='Input'><Member Name='X' Datatype='Int' Accessibility='Public'/></Section>";
            var ret = "<Section Name='Return'><Member Name='Ret_Val' Datatype='" + type + "' Accessibility='Public'/></Section>";
            Assert.Equal(source.Interface, ProjectGenerationSclProjection.XmlInterface("<Interface><Sections>" + input + ret + "</Sections></Interface>", "FC"));
            Assert.Equal(source.Interface, ProjectGenerationSclProjection.XmlInterface("<Interface><Sections>" + ret + input + "</Sections></Interface>", "FC"));
        }

        [Theory]
        [InlineData("VAR RETAIN\nState : Bool;\nEND_VAR")]
        [InlineData("VAR\nState : Bool := TRUE;\nEND_VAR")]
        public void UnqualifiedStorageDeclarationsAreRejectedBeforeCreation(string declarations)
        {
            Assert.Throws<NotSupportedException>(() => ProjectGenerationSclProjection.Parse("FUNCTION_BLOCK \"F\"\nVERSION : 0.1\n" + declarations + "\nBEGIN\nEND_FUNCTION_BLOCK"));
        }
    }
}
