using System;
using System.IO;
using System.Text;
using System.Xml;
using TiaGuard.Openness;

namespace TiaGuard.Contracts.Tests
{
    internal sealed class LadFixture : IDisposable
    {
        internal readonly string OwnedRoot = Path.Combine(Path.GetTempPath(), "TiaGuard.LadTests", Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(OwnedRoot, "tia-source");
        internal RoundTripBuildInput Input;
        internal XmlDocument Document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        internal XmlNamespaceManager Ns;
        internal LadFixture()
        {
            Directory.CreateDirectory(OwnedRoot);
            FileSystemSafety.CopyPlainTree(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "lad-v21-source"), Root);
            Input = RoundTripBuildInput.LoadSource(Root); Document.Load(Input.BlockSourcePath);
            Ns = new XmlNamespaceManager(Document.NameTable); Ns.AddNamespace("f", AiEngineeringLadParser.FlgNetNamespace);
        }
        internal XmlElement At(string xpath) => (XmlElement)Document.SelectSingleNode(xpath, Ns);
        internal const string Network1 = "/Document/SW.Blocks.OB/ObjectList/SW.Blocks.CompileUnit[1]";
        internal const string Network2 = "/Document/SW.Blocks.OB/ObjectList/SW.Blocks.CompileUnit[2]";
        internal const string Network3 = "/Document/SW.Blocks.OB/ObjectList/SW.Blocks.CompileUnit[3]";
        internal XmlElement Part(int uid) => At(Network1 + "//f:Part[@UId='" + uid + "']");
        internal XmlElement Wire(int uid) => At(Network1 + "//f:Wire[@UId='" + uid + "']");
        internal XmlElement Access(int uid) => At(Network1 + "//f:Access[@UId='" + uid + "']");
        internal void Save()
        {
            var xml = Document.OuterXml;
            File.WriteAllText(Input.BlockSourcePath, xml, new UTF8Encoding(false));
            Input.Block.Source.Sha256 = CanonicalFixture.Hash(xml);
            File.WriteAllText(Path.Combine(Root, Input.Plc.Blocks[0]), RoundTripJson.Serialize(Input.Block), new UTF8Encoding(false));
        }
        internal AiEngineeringView View() { Save(); return AiEngineeringRenderer.Read(Root); }
        public void Dispose() { FileSystemSafety.DeleteOwnedTree(OwnedRoot); }
    }
}
