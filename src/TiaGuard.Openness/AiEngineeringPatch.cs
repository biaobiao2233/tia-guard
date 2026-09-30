using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaGuard.Openness
{
    // Plans a bounded engineering patch against canonical tia-source and proves the
    // expected graph by running the existing AI Engineering v2 renderer. It does not
    // call TIA Portal and it does not invent a second LAD parser.
    public static class AiEngineeringPatch
    {
        public const string SchemaVersion = "tia-guard.engineering-patch/v1";

        public static void DeleteOwnedTree(string path)
        {
            FileSystemSafety.DeleteOwnedTree(path);
        }

        public static void DeleteOwnedDirectory(string path)
        {
            FileSystemSafety.DeleteOwnedTree(path);
        }

        public static string Canonicalize(string patchJson)
        {
            var request = ReadRequest(patchJson);
            return Serialize(request).Trim();
        }

        public static AiPatchPlan Plan(string sourceRoot, string patchJson, string previewDirectory)
        {
            if (string.IsNullOrWhiteSpace(previewDirectory))
                throw new ArgumentException("A preview directory is required.", nameof(previewDirectory));
            var request = ReadRequest(patchJson);
            var canonical = Serialize(request).Trim();
            var before = AiEngineeringRenderer.Read(sourceRoot);
            FileSystemSafety.CopyPlainTree(sourceRoot, previewDirectory);
            if (request.Operation == "upsert_tag")
                return PlanTag(before, request, canonical);
            if (request.Operation == "replace_output_condition")
                return PlanLogic(before, request, canonical, previewDirectory);
            throw Reject("OPERATION_UNSUPPORTED: " + request.Operation);
        }

        public static string Fingerprint(AiEngineeringView view, AiPatchRequest request)
        {
            if (request.Operation == "upsert_tag")
            {
                var table = FindTable(view, request.Target.Table);
                var tag = table.Tags.FirstOrDefault(item =>
                    string.Equals(item.Name, request.Target.Tag, StringComparison.Ordinal));
                var body = request.Target.Table + "|" + request.Target.Tag + "|" +
                    (tag == null ? "absent" : tag.DataType + "|" + tag.Address);
                return Sha256(body);
            }
            var block = AiEngineeringContextQuery.ResolveBlock(view, request.Target.Block);
            var network = AiEngineeringContextQuery.ResolveNetwork(block, request.Target.Network);
            return Sha256(Serialize(network.Analysis).Trim() + "|" +
                (network.Graph == null ? "no-graph" : network.Graph.Status) + "|" +
                network.Id);
        }

        private static AiPatchPlan PlanTag(AiEngineeringView before, AiPatchRequest request, string canonical)
        {
            var target = request.Target ?? new AiPatchTarget();
            if (string.IsNullOrWhiteSpace(target.Table) || string.IsNullOrWhiteSpace(target.Tag))
                throw Reject("TAG_TARGET_REQUIRED");
            if (!Regex.IsMatch(target.Tag, "^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
                throw Reject("TAG_NAME_UNSUPPORTED");
            if (!string.Equals(request.DataType, "Bool", StringComparison.Ordinal))
                throw Reject("TAG_DATATYPE_UNSUPPORTED");
            if (!RoundTripProfile.SupportsTag(request.DataType, request.LogicalAddress))
                throw Reject("TAG_ADDRESS_UNSUPPORTED");
            var table = FindTable(before, target.Table);
            var existing = table.Tags.FirstOrDefault(item =>
                string.Equals(item.Name, target.Tag, StringComparison.Ordinal));
            var address = SnapshotAddressParser.Parse(request.LogicalAddress);
            foreach (var tag in before.TagTables.SelectMany(item => item.Tags))
            {
                if (existing != null && string.Equals(tag.Name, existing.Name, StringComparison.Ordinal) &&
                    string.Equals(table.Name, target.Table, StringComparison.Ordinal))
                    continue;
                var other = SnapshotAddressParser.Parse(tag.Address);
                if (AiEngineeringLadParser.Overlap(address.Raw ?? request.LogicalAddress, other.Raw ?? tag.Address))
                    throw Reject("TAG_ADDRESS_OVERLAP: " + tag.Name);
            }
            return new AiPatchPlan
            {
                SchemaVersion = SchemaVersion,
                Operation = request.Operation,
                CanonicalPatch = canonical,
                Fingerprint = Fingerprint(before, request),
                Validation = Accepted(),
                Target = target,
                Output = null,
                SavesProject = false,
                Publishes = false,
                MutatesDisposableOfflineCopy = true,
                AffectedSourceRefs = new List<string> { table.SourceRef },
                CurrentTag = existing == null
                    ? new AiPatchTagState { Table = table.Name, Name = target.Tag, Exists = false }
                    : TagState(table.Name, existing, true),
                ExpectedTag = new AiPatchTagState
                {
                    Table = table.Name,
                    Name = target.Tag,
                    Exists = true,
                    DataType = request.DataType,
                    LogicalAddress = request.LogicalAddress
                }
            };
        }

        private static AiPatchPlan PlanLogic(
            AiEngineeringView before, AiPatchRequest request, string canonical, string previewDirectory)
        {
            var target = request.Target ?? new AiPatchTarget();
            AiBlock block;
            AiNetwork network;
            try
            {
                block = AiEngineeringContextQuery.ResolveBlock(before, target.Block);
                network = AiEngineeringContextQuery.ResolveNetwork(block, target.Network);
            }
            catch (ArgumentException error)
            {
                throw Reject(error.Message.StartsWith("AI_CONTEXT_REJECTED: ", StringComparison.Ordinal)
                    ? error.Message.Substring("AI_CONTEXT_REJECTED: ".Length)
                    : error.Message);
            }
            if (network.Analysis == null || network.Analysis.Status != "supported" ||
                network.Graph == null || network.Graph.Status != "complete")
                throw Reject("GRAPH_NOT_REVERSE_MAPPED");
            if (network.Analysis.Writes.Count != 1)
                throw Reject("PARALLEL_TOPOLOGY_NOT_REVERSE_MAPPED");
            var write = network.Analysis.Writes[0];
            if (write.Target == null || !string.Equals(write.Target.Symbol, request.Output, StringComparison.Ordinal))
                throw Reject("OUTPUT_TARGET_MISMATCH");
            var terms = Flatten(request.Expression);
            if (terms.Count == 0)
                throw Reject("EXPRESSION_EMPTY");
            if (terms.Any(term => string.Equals(term.Symbol, request.Output, StringComparison.Ordinal)))
                throw Reject("OUTPUT_SYMBOL_IN_CONDITION_UNSUPPORTED");
            var duplicates = terms.GroupBy(term => term.Symbol, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
            if (duplicates != null)
                throw Reject("DUPLICATE_SYMBOL: " + duplicates.Key);
            var symbols = before.TagTables.SelectMany(table => table.Tags).ToList();
            foreach (var term in terms)
            {
                var tag = symbols.FirstOrDefault(item => string.Equals(item.Name, term.Symbol, StringComparison.Ordinal));
                if (tag == null)
                    throw Reject("UNKNOWN_SYMBOL: " + term.Symbol);
                if (!string.Equals(tag.DataType, "Bool", StringComparison.Ordinal))
                    throw Reject("SYMBOL_DATATYPE_UNSUPPORTED: " + term.Symbol);
            }
            var coil = symbols.FirstOrDefault(item => string.Equals(item.Name, request.Output, StringComparison.Ordinal));
            if (coil == null || !string.Equals(coil.DataType, "Bool", StringComparison.Ordinal))
                throw Reject("UNKNOWN_SYMBOL: " + request.Output);

            var preview = RoundTripBuildInput.LoadSource(previewDirectory);
            var xmlPath = preview.BlockSourcePath;
            var previousHash = preview.Block.Source.Sha256;
            ReplaceFlgNet(xmlPath, network.Ordinal, BuildSerialFlgNet(terms, request.Output));
            ReplaceHashOnce(Path.Combine(Path.GetDirectoryName(xmlPath), "block.json"), previousHash, Sha256File(xmlPath));
            var after = AiEngineeringRenderer.Read(previewDirectory);
            var afterBlock = AiEngineeringContextQuery.ResolveBlock(after, target.Block);
            var afterNetwork = AiEngineeringContextQuery.ResolveNetwork(afterBlock, target.Network);
            if (afterNetwork.Analysis == null || afterNetwork.Analysis.Status != "supported" ||
                afterNetwork.Analysis.Writes.Count != 1)
                throw Reject("PATCH_DID_NOT_ROUND_TRIP");
            if (!ExpressionEquals(ExpectedExpression(terms), afterNetwork.Analysis.Writes[0].Expression))
                throw Reject("PATCH_DID_NOT_ROUND_TRIP");
            if (!string.Equals(afterNetwork.Analysis.Writes[0].Target.Symbol, request.Output, StringComparison.Ordinal))
                throw Reject("PATCH_DID_NOT_ROUND_TRIP");
            AssertNeighborsUnchanged(before, after, network.Ordinal);
            return new AiPatchPlan
            {
                SchemaVersion = SchemaVersion,
                Operation = request.Operation,
                CanonicalPatch = canonical,
                Fingerprint = Fingerprint(before, request),
                Validation = Accepted(),
                Target = target,
                Output = request.Output,
                SavesProject = false,
                Publishes = false,
                MutatesDisposableOfflineCopy = true,
                AffectedSourceRefs = new List<string> { network.SourceRef, block.XmlSourceRef },
                CurrentNetwork = network,
                ExpectedNetwork = afterNetwork,
                ModifiedBlockXmlPath = xmlPath
            };
        }

        public static void AssertNeighborsUnchanged(AiEngineeringView before, AiEngineeringView after, int ordinal)
        {
            if (before.Blocks.Count != after.Blocks.Count)
                throw Reject("NEIGHBOR_NETWORK_CHANGED");
            for (var i = 0; i < before.Blocks.Count; i++)
            {
                var left = before.Blocks[i];
                var right = after.Blocks[i];
                if (left.Networks.Count != right.Networks.Count)
                    throw Reject("NEIGHBOR_NETWORK_CHANGED");
                for (var n = 0; n < left.Networks.Count; n++)
                {
                    if (left.Networks[n].Ordinal == ordinal)
                        continue;
                    if (!string.Equals(Serialize(left.Networks[n].Analysis).Trim(),
                            Serialize(right.Networks[n].Analysis).Trim(), StringComparison.Ordinal))
                        throw Reject("NEIGHBOR_NETWORK_CHANGED");
                }
            }
        }

        public static bool ExpressionEquals(AiLadExpression left, AiLadExpression right)
        {
            if (left == null || right == null) return left == right;
            if (!string.Equals(left.Op, right.Op, StringComparison.Ordinal)) return false;
            if (!string.Equals(left.Symbol ?? string.Empty, right.Symbol ?? string.Empty, StringComparison.Ordinal))
                return false;
            var a = left.Args ?? new List<AiLadExpression>();
            var b = right.Args ?? new List<AiLadExpression>();
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (!ExpressionEquals(a[i], b[i])) return false;
            return true;
        }

        private static AiLadExpression ExpectedExpression(IReadOnlyList<Term> terms)
        {
            var args = terms.Select(term => term.Negated
                ? new AiLadExpression
                {
                    Op = "not",
                    Args = new List<AiLadExpression> { new AiLadExpression { Op = "read", Symbol = term.Symbol } }
                }
                : new AiLadExpression { Op = "read", Symbol = term.Symbol }).ToList();
            return args.Count == 1 ? args[0] : new AiLadExpression { Op = "and", Args = args };
        }

        private static List<Term> Flatten(AiLadExpression expression)
        {
            if (expression == null || string.IsNullOrWhiteSpace(expression.Op))
                throw Reject("EXPRESSION_REQUIRED");
            if (expression.Op == "read")
            {
                if (expression.Args != null && expression.Args.Count != 0)
                    throw Reject("READ_SHAPE_UNSUPPORTED");
                if (string.IsNullOrWhiteSpace(expression.Symbol) ||
                    !Regex.IsMatch(expression.Symbol, "^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
                    throw Reject("SYMBOL_NAME_UNSUPPORTED");
                return new List<Term> { new Term(expression.Symbol, false) };
            }
            if (expression.Op == "not")
            {
                if (expression.Args == null || expression.Args.Count != 1 || expression.Args[0] == null ||
                    expression.Args[0].Op != "read")
                    throw Reject("NOT_ONLY_OVER_READ");
                var inner = Flatten(expression.Args[0]);
                if (inner.Count != 1 || inner[0].Negated)
                    throw Reject("NOT_ONLY_OVER_READ");
                return new List<Term> { new Term(inner[0].Symbol, true) };
            }
            if (expression.Op == "and")
            {
                if (expression.Args == null || expression.Args.Count == 0)
                    throw Reject("AND_SHAPE_UNSUPPORTED");
                var terms = new List<Term>();
                foreach (var arg in expression.Args)
                    terms.AddRange(Flatten(arg));
                return terms;
            }
            throw Reject("OPERATOR_UNSUPPORTED: " + expression.Op);
        }

        private static string BuildSerialFlgNet(IReadOnlyList<Term> terms, string output)
        {
            var xml = new StringBuilder();
            var uid = 21;
            var access = new List<int>();
            xml.Append("<FlgNet xmlns=\"http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5\"><Parts>");
            foreach (var term in terms)
            {
                access.Add(uid);
                xml.Append("<Access Scope=\"GlobalVariable\" UId=\"")
                    .Append(uid.ToString(CultureInfo.InvariantCulture))
                    .Append("\"><Symbol><Component Name=\"")
                    .Append(term.Symbol)
                    .Append("\" /></Symbol></Access>");
                uid++;
            }
            var coilAccess = uid++;
            xml.Append("<Access Scope=\"GlobalVariable\" UId=\"")
                .Append(coilAccess.ToString(CultureInfo.InvariantCulture))
                .Append("\"><Symbol><Component Name=\"")
                .Append(output)
                .Append("\" /></Symbol></Access>");
            var contacts = new List<int>();
            foreach (var term in terms)
            {
                contacts.Add(uid);
                xml.Append("<Part Name=\"Contact\" UId=\"")
                    .Append(uid.ToString(CultureInfo.InvariantCulture))
                    .Append("\"");
                if (term.Negated)
                    xml.Append("><Negated Name=\"operand\" /></Part>");
                else
                    xml.Append(" />");
                uid++;
            }
            var coil = uid++;
            xml.Append("<Part Name=\"Coil\" UId=\"")
                .Append(coil.ToString(CultureInfo.InvariantCulture))
                .Append("\" /></Parts><Wires>");
            xml.Append(Wire(ref uid, "<Powerrail /><NameCon UId=\"" +
                contacts[0].ToString(CultureInfo.InvariantCulture) + "\" Name=\"in\" />"));
            for (var i = 0; i < terms.Count; i++)
                xml.Append(Wire(ref uid, "<IdentCon UId=\"" + access[i].ToString(CultureInfo.InvariantCulture) +
                    "\" /><NameCon UId=\"" + contacts[i].ToString(CultureInfo.InvariantCulture) + "\" Name=\"operand\" />"));
            for (var i = 0; i < contacts.Count - 1; i++)
                xml.Append(Wire(ref uid, "<NameCon UId=\"" + contacts[i].ToString(CultureInfo.InvariantCulture) +
                    "\" Name=\"out\" /><NameCon UId=\"" + contacts[i + 1].ToString(CultureInfo.InvariantCulture) +
                    "\" Name=\"in\" />"));
            xml.Append(Wire(ref uid, "<NameCon UId=\"" + contacts[contacts.Count - 1].ToString(CultureInfo.InvariantCulture) +
                "\" Name=\"out\" /><NameCon UId=\"" + coil.ToString(CultureInfo.InvariantCulture) + "\" Name=\"in\" />"));
            xml.Append(Wire(ref uid, "<IdentCon UId=\"" + coilAccess.ToString(CultureInfo.InvariantCulture) +
                "\" /><NameCon UId=\"" + coil.ToString(CultureInfo.InvariantCulture) + "\" Name=\"operand\" />"));
            xml.Append("</Wires></FlgNet>");
            return xml.ToString();
        }

        private static string Wire(ref int uid, string body)
        {
            var wire = "<Wire UId=\"" + uid.ToString(CultureInfo.InvariantCulture) + "\">" + body + "</Wire>";
            uid++;
            return wire;
        }

        private static void ReplaceFlgNet(string xmlPath, int ordinal, string flgNet)
        {
            var xml = File.ReadAllText(xmlPath);
            const string token = "<SW.Blocks.CompileUnit";
            var search = 0;
            var unitStart = -1;
            for (var i = 1; i <= ordinal; i++)
            {
                unitStart = xml.IndexOf(token, search, StringComparison.Ordinal);
                if (unitStart < 0)
                    throw Reject("NETWORK_XML_NOT_FOUND");
                search = unitStart + token.Length;
            }
            var next = xml.IndexOf(token, search, StringComparison.Ordinal);
            var regionEnd = next < 0 ? xml.Length : next;
            var start = xml.IndexOf("<FlgNet", unitStart, StringComparison.Ordinal);
            var end = start < 0 ? -1 : xml.IndexOf("</FlgNet>", start, StringComparison.Ordinal);
            if (start < 0 || end < 0 || start >= regionEnd || end >= regionEnd)
                throw Reject("NETWORK_XML_NOT_FOUND");
            end += "</FlgNet>".Length;
            var updated = xml.Substring(0, start) + flgNet + xml.Substring(end);
            File.WriteAllText(xmlPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static void ReplaceHashOnce(string blockJsonPath, string previousHash, string nextHash)
        {
            var json = File.ReadAllText(blockJsonPath);
            var marker = "\"" + previousHash + "\"";
            var first = json.IndexOf(marker, StringComparison.Ordinal);
            if (first < 0 || json.IndexOf(marker, first + marker.Length, StringComparison.Ordinal) >= 0)
                throw Reject("BLOCK_HASH_UPDATE_AMBIGUOUS");
            json = json.Substring(0, first) + "\"" + nextHash + "\"" + json.Substring(first + marker.Length);
            File.WriteAllText(blockJsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static AiTagTable FindTable(AiEngineeringView view, string name)
        {
            var table = view.TagTables.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.Ordinal));
            if (table == null)
                throw Reject("UNKNOWN_TAG_TABLE: " + name);
            return table;
        }

        private static AiPatchTagState TagState(string table, AiTag tag, bool exists)
        {
            return new AiPatchTagState
            {
                Table = table,
                Name = tag.Name,
                Exists = exists,
                DataType = tag.DataType,
                LogicalAddress = tag.Address,
                SourceRef = tag.SourceRef
            };
        }

        private static AiPatchValidation Accepted()
        {
            return new AiPatchValidation { Status = "accepted", Diagnostics = new List<string>() };
        }

        private static AiPatchRequest ReadRequest(string patchJson)
        {
            if (string.IsNullOrWhiteSpace(patchJson))
                throw Reject("PATCH_REQUIRED");
            AiPatchRequest request;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(patchJson)))
                    request = (AiPatchRequest)new DataContractJsonSerializer(typeof(AiPatchRequest)).ReadObject(stream);
            }
            catch (Exception error)
            {
                throw Reject("SCHEMA: " + error.Message);
            }
            if (request == null || request.SchemaVersion != SchemaVersion)
                throw Reject("SCHEMA_VERSION");
            if (request.Target == null)
                throw Reject("TARGET_REQUIRED");
            return request;
        }

        private static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static string Sha256File(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static InvalidOperationException Reject(string code)
        {
            return new InvalidOperationException("AI_PATCH_REJECTED: " + code);
        }

        private sealed class Term
        {
            public Term(string symbol, bool negated)
            {
                Symbol = symbol;
                Negated = negated;
            }

            public string Symbol { get; private set; }
            public bool Negated { get; private set; }
        }
    }

    [DataContract]
    public sealed class AiPatchRequest
    {
        [DataMember(Name = "schemaVersion", Order = 0)] public string SchemaVersion;
        [DataMember(Name = "operation", Order = 1)] public string Operation;
        [DataMember(Name = "target", Order = 2)] public AiPatchTarget Target;
        [DataMember(Name = "output", Order = 3)] public string Output;
        [DataMember(Name = "expression", Order = 4)] public AiLadExpression Expression;
        [DataMember(Name = "dataType", Order = 5)] public string DataType;
        [DataMember(Name = "logicalAddress", Order = 6)] public string LogicalAddress;
    }

    [DataContract]
    public sealed class AiPatchTarget
    {
        [DataMember(Name = "block", Order = 0)] public string Block;
        [DataMember(Name = "network", Order = 1)] public int Network;
        [DataMember(Name = "table", Order = 2)] public string Table;
        [DataMember(Name = "tag", Order = 3)] public string Tag;
    }

    [DataContract]
    public sealed class AiPatchPlan
    {
        [DataMember(Name = "schemaVersion", Order = 0)] public string SchemaVersion;
        [DataMember(Name = "operation", Order = 1)] public string Operation;
        [DataMember(Name = "canonicalPatch", Order = 2)] public string CanonicalPatch;
        [DataMember(Name = "fingerprint", Order = 3)] public string Fingerprint;
        [DataMember(Name = "validation", Order = 4)] public AiPatchValidation Validation;
        [DataMember(Name = "target", Order = 5)] public AiPatchTarget Target;
        [DataMember(Name = "output", Order = 6)] public string Output;
        [DataMember(Name = "affectedSourceRefs", Order = 7)] public List<string> AffectedSourceRefs = new List<string>();
        [DataMember(Name = "currentNetwork", Order = 8)] public AiNetwork CurrentNetwork;
        [DataMember(Name = "expectedNetwork", Order = 9)] public AiNetwork ExpectedNetwork;
        [DataMember(Name = "currentTag", Order = 10)] public AiPatchTagState CurrentTag;
        [DataMember(Name = "expectedTag", Order = 11)] public AiPatchTagState ExpectedTag;
        [DataMember(Name = "savesProject", Order = 12)] public bool SavesProject;
        [DataMember(Name = "publishes", Order = 13)] public bool Publishes;
        [DataMember(Name = "mutatesDisposableOfflineCopy", Order = 14)] public bool MutatesDisposableOfflineCopy;
        public string ModifiedBlockXmlPath { get; set; }
    }

    [DataContract]
    public sealed class AiPatchValidation
    {
        [DataMember(Name = "status", Order = 0)] public string Status;
        [DataMember(Name = "diagnostics", Order = 1)] public List<string> Diagnostics = new List<string>();
    }

    [DataContract]
    public sealed class AiPatchApplyResult
    {
        [DataMember(Name = "status", Order = 0)] public string Status;
        [DataMember(Name = "operation", Order = 1)] public string Operation;
        [DataMember(Name = "savedOriginalProject", Order = 2)] public bool SavedOriginalProject;
        [DataMember(Name = "savedDisposableCopy", Order = 3)] public bool SavedDisposableCopy;
        [DataMember(Name = "published", Order = 4)] public bool Published;
        [DataMember(Name = "compileErrors", Order = 5)] public int CompileErrors;
        [DataMember(Name = "compileWarnings", Order = 6)] public int CompileWarnings;
        [DataMember(Name = "verifyVerdict", Order = 7)] public string VerifyVerdict;
        [DataMember(Name = "contentId", Order = 8)] public string ContentId;
        [DataMember(Name = "resultingNetwork", Order = 9)] public AiNetwork ResultingNetwork;
        [DataMember(Name = "resultingTag", Order = 10)] public AiPatchTagState ResultingTag;
        [DataMember(Name = "reason", Order = 11)] public string Reason;
        [DataMember(Name = "epoch", Order = 12)] public int Epoch;
    }

    [DataContract]
    public sealed class AiPatchTagState
    {
        [DataMember(Name = "table", Order = 0)] public string Table;
        [DataMember(Name = "name", Order = 1)] public string Name;
        [DataMember(Name = "exists", Order = 2)] public bool Exists;
        [DataMember(Name = "dataType", Order = 3)] public string DataType;
        [DataMember(Name = "logicalAddress", Order = 4)] public string LogicalAddress;
        [DataMember(Name = "sourceRef", Order = 5)] public string SourceRef;
    }
}
