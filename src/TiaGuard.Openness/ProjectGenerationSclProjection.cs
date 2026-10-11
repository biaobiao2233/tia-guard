using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TiaGuard.Openness
{
    // A comparison projection, not a compiler. TIA remains responsible for SCL syntax.
    // Names, literals and every non-comment token are preserved; only formatting is ignored.
    public static class ProjectGenerationSclProjection
    {
        public const string Version = "scl-source-v2";
        public sealed class SourceObject
        {
            public string Kind { get; internal set; }
            public string Name { get; internal set; }
            public string Text { get; internal set; }
            public string InstanceOfName { get; internal set; }
            public string Interface { get; internal set; }
        }
        private sealed class Token
        {
            internal string Value;
            internal string Raw;
            internal int Start, End;
            internal bool Quoted;
        }
        private static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>
        { ["ORGANIZATION_BLOCK"] = "OB", ["FUNCTION_BLOCK"] = "FB", ["FUNCTION"] = "FC", ["DATA_BLOCK"] = "DB", ["TYPE"] = "UDT" };
        private static readonly Dictionary<string, string> Sections = new Dictionary<string, string>
        { ["VAR_INPUT"] = "Input", ["VAR_OUTPUT"] = "Output", ["VAR_IN_OUT"] = "InOut",
            ["VAR"] = "Static", ["VAR_TEMP"] = "Temp", ["VAR_CONSTANT"] = "Constant" };

        public static IReadOnlyList<SourceObject> Parse(string text)
        {
            var tokens = Lex(text); var objects = new List<SourceObject>();
            for (int i = 0; i < tokens.Count;)
            {
                if (tokens[i].Value == ";") { i++; continue; }
                var first = tokens[i];
                if (first.Quoted || !Kinds.TryGetValue(first.Value, out var kind)) throw new InvalidDataException("Unexpected text outside SCL object.");
                var endName = "END_" + first.Value;
                if (++i >= tokens.Count) throw new InvalidDataException("Missing SCL object name.");
                var name = Identifier(tokens[i++]); var bodyStart = i;
                while (i < tokens.Count && (tokens[i].Quoted || tokens[i].Value != endName)) i++;
                if (i == tokens.Count) throw new InvalidDataException("Missing " + endName);
                var body = tokens.GetRange(bodyStart, i - bodyStart);
                var instance = kind == "DB" ? Instance(body) : null;
                objects.Add(new SourceObject { Kind = kind, Name = name,
                    Text = text.Substring(first.Start, tokens[i].End - first.Start),
                    InstanceOfName = instance, Interface = Interface(body, kind, name) });
                i++;
            }
            if (objects.Count == 0) throw new InvalidDataException("No SCL objects.");
            return objects;
        }

        public static string Normalize(string text)
        {
            var tokens = Lex(text);
            var begin = tokens.FindIndex(t => !t.Quoted && t.Value == "BEGIN");
            if (begin >= 0)
            {
                var locals = new HashSet<string>(StringComparer.Ordinal);
                bool inDeclarations = false;
                for (int i = 2; i < begin; i++)
                {
                    if (!tokens[i].Quoted && Sections.ContainsKey(tokens[i].Value)) inDeclarations = true;
                    else if (!tokens[i].Quoted && tokens[i].Value == "END_VAR") inDeclarations = false;
                    else if (inDeclarations && !tokens[i].Quoted && i + 1 < begin && tokens[i + 1].Value == ":") locals.Add(tokens[i].Value);
                }
                if (tokens.Count >= 2 && tokens[0].Value == "FUNCTION") locals.Add(Identifier(tokens[1]).ToUpperInvariant());
                if (tokens.Count >= 2 && tokens[0].Value == "ORGANIZATION_BLOCK" && Identifier(tokens[1]) == "Main")
                { locals.Add("INITIAL_CALL"); locals.Add("REMANENCE"); }
                // V21 qualifies unquoted references to declared locals with '#'. Only
                // declaration-bound locals in the body qualify; global quoted names,
                // strings, field selectors and already-qualified locals remain distinct.
                for (int i = tokens.Count - 1; i > begin; i--)
                    if (!tokens[i].Quoted && locals.Contains(tokens[i].Value) && tokens[i - 1].Value != "#" && tokens[i - 1].Value != ".")
                        tokens.Insert(i, new Token { Value = "#" });
            }
            // Observed on the owned V21/1212C cyclic OB1: TIA adds this default
            // title if omitted. Insert into the expected representation too; never
            // discard a user title or an arbitrary exported title.
            if (tokens.Count >= 2 && tokens[0].Value == "ORGANIZATION_BLOCK" && Identifier(tokens[1]) == "Main" &&
                !tokens.TakeWhile(t => t.Quoted || t.Value != "BEGIN").Any(t => !t.Quoted && t.Value == "TITLE"))
                tokens.InsertRange(2, new[] { new Token { Value = "TITLE" }, new Token { Value = "=" },
                    new Token { Value = "\"Main Program Sweep (Cycle)\"", Quoted = true } });
            // Empty VAR sections are the only declaration default removed. Nonempty
            // sections, attributes, version, declarations, instructions and literals remain.
            for (int i = tokens.Count - 2; i >= 0; i--)
                if (!tokens[i].Quoted && Sections.ContainsKey(tokens[i].Value) && tokens[i + 1].Value == "END_VAR")
                    tokens.RemoveRange(i, 2);
            return Pack(tokens.Select(t => t.Value));
        }

        public static string CyclicObInterface(string declared) => Pack(new[] {
            "Input/INITIAL_CALL:" + Pack(new[] { "BOOL" }),
            "Input/REMANENCE:" + Pack(new[] { "BOOL" }) }) + declared;

        // WithDefaults is required by the readback adapter. Only the independently
        // qualified V21 defaults below are accepted; an unknown control attribute
        // or value is UNKNOWN, never silently discarded from a PASS projection.
        public static string XmlInterface(string xml, string objectKind = null, bool instanceDb = false)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                var root = XElement.Load(reader);
                RequireShape(root, "Interface", new string[0], new[] { "Sections" });
                if (root.Elements().Count() != 1) throw new InvalidDataException("Exactly one interface Sections is required.");
                var sections = root.Elements().Single();
                RequireShape(sections, "Sections", new string[0], new[] { "Section" });
                var rows = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var section in sections.Elements())
                {
                    RequireShape(section, "Section", new[] { "Name" }, new[] { "Member" });
                    var sectionName = (string)section.Attribute("Name");
                    if (sectionName == "None" && objectKind == "UDT") sectionName = "Static";
                    if (!Sections.Values.Contains(sectionName) && sectionName != "Return") throw new InvalidDataException("Unsupported observed interface section: " + sectionName);
                    if (!seen.Add(sectionName)) throw new InvalidDataException("Duplicate interface section.");
                    foreach (var member in section.Elements()) XmlMember(member, sectionName, rows, objectKind, instanceDb, false);
                }
                return OrderedInterface(rows);
            }
        }
        private static void XmlMember(XElement member, string path, List<string> rows, string kind, bool instanceDb, bool expanded)
        {
            RequireShape(member, "Member", new[] { "Name", "Datatype", "Remanence", "Accessibility", "Informative" },
                new[] { "AttributeList", "Comment", "Sections" });
            var name = (string)member.Attribute("Name"); var type = (string)member.Attribute("Datatype");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) throw new InvalidDataException("Unreadable interface member.");
            ValidateDefaults(member, kind, instanceDb, path, name, expanded);
            path += "/" + name.ToUpperInvariant();
            if (rows.Any(r => r.StartsWith(path + ":", StringComparison.Ordinal))) throw new InvalidDataException("Duplicate interface member.");
            rows.Add(path + ":" + Pack(Lex(type).Select(t => t.Value)));
            foreach (var child in member.Elements())
            {
                if (child.Name.LocalName == "Comment")
                {
                    RequireShape(child, "Comment", new string[0], new[] { "MultiLanguageText" });
                    foreach (var text in child.Elements()) RequireShape(text, "MultiLanguageText", new[] { "Lang" }, new string[0], true);
                }
                else if (child.Name.LocalName == "Sections")
                {
                    RequireShape(child, "Sections", new string[0], new[] { "Section" });
                    if (child.Elements().Count() != 1) throw new InvalidDataException("Unsupported expanded interface shape.");
                    var section = child.Elements().Single();
                    RequireShape(section, "Section", new[] { "Name" }, new[] { "Member" });
                    if ((string)section.Attribute("Name") != "None") throw new InvalidDataException("Unsupported nested interface section.");
                    foreach (var nested in section.Elements()) XmlMember(nested, path, rows, kind, instanceDb, true);
                }
            }
        }

        private static void RequireShape(XElement element, string name, string[] attributes, string[] children, bool text = false)
        {
            if (element.Name.LocalName != name || element.Attributes().Any(a => !a.IsNamespaceDeclaration &&
                (a.Name.NamespaceName != "" || !attributes.Contains(a.Name.LocalName))) ||
                element.Elements().Any(e => !children.Contains(e.Name.LocalName)) ||
                element.Nodes().Any(n => !(n is XElement) && !(n is XText t && (text || string.IsNullOrWhiteSpace(t.Value)))))
                throw new InvalidDataException("Unsupported observed interface structure: " + element.Name.LocalName);
            var ns = element.Name.NamespaceName;
            if (ns != "" && ns != "http://www.siemens.com/automation/Openness/SW/Interface/v5")
                throw new InvalidDataException("Unqualified interface schema: " + ns);
            if (element.Elements().GroupBy(e => e.Name.LocalName).Any(g => g.Count() > 1 &&
                !new[] { "Section", "Member", "MultiLanguageText", "BooleanAttribute" }.Contains(g.Key)))
                throw new InvalidDataException("Duplicate interface container.");
        }

        private static void ValidateDefaults(XElement member, string kind, bool instanceDb, string path, string name, bool expanded)
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            var flags = new Dictionary<string, string>(StringComparer.Ordinal);
            var section = path.Split('/')[0];
            if (kind == null)
            {
                // Shape-only helper for standalone parser callers. Production Compare
                // always supplies an independently compared, supported object kind.
                if (member.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "Name" && a.Name.LocalName != "Datatype") || member.Elements().Any(e => e.Name.LocalName == "AttributeList"))
                    throw new InvalidDataException("Metadata requires an explicit qualified object profile.");
                return;
            }
            if (!new[] { "OB", "FC", "FB", "DB", "UDT" }.Contains(kind)) throw new InvalidDataException("Unknown interface object kind.");
            if (!expanded && section != "Temp" && section != "Constant")
            {
                if (kind == "OB")
                {
                    if (section != "Input" || !new[] { "Initial_Call", "Remanence" }.Contains(name)) throw new InvalidDataException("Unqualified OB interface member.");
                    expected["Accessibility"] = "Public"; expected["Informative"] = "true";
                }
                else if (kind == "FC") expected["Accessibility"] = "Public";
                else
                {
                    if (kind != "UDT") { expected["Remanence"] = "NonRetain"; expected["Accessibility"] = "Public"; }
                    flags["ExternalAccessible"] = "true"; flags["ExternalVisible"] = "true"; flags["ExternalWritable"] = "true";
                    if (section == "Static") flags["SetPoint"] = kind == "DB" && !instanceDb ? "true" : "false";
                }
            }
            foreach (var attr in new[] { "Remanence", "Accessibility", "Informative" })
                if ((string)member.Attribute(attr) != (expected.TryGetValue(attr, out var value) ? value : null))
                    throw new InvalidDataException("Unqualified member " + attr + ": " + path + "/" + name);
            var lists = member.Elements().Where(e => e.Name.LocalName == "AttributeList").ToList();
            if (lists.Count != (flags.Count == 0 ? 0 : 1)) throw new InvalidDataException("Missing or unexpected member default attributes.");
            if (lists.Count == 0) return;
            RequireShape(lists[0], "AttributeList", new string[0], new[] { "BooleanAttribute" });
            var actual = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var flag in lists[0].Elements())
            {
                RequireShape(flag, "BooleanAttribute", new[] { "Name", "SystemDefined" }, new string[0], true);
                var flagName = (string)flag.Attribute("Name");
                if ((string)flag.Attribute("SystemDefined") != "true" || flagName == null || actual.ContainsKey(flagName))
                    throw new InvalidDataException("Unqualified member BooleanAttribute.");
                actual.Add(flagName, flag.Value);
            }
            if (actual.Count != flags.Count || flags.Any(f => !actual.TryGetValue(f.Key, out var v) || v != f.Value))
                throw new InvalidDataException("Member access/default attributes differ from qualified V21 defaults.");
        }

        // Expand named UDT members from frozen source declarations, never observed XML.
        public static string ExpandTypes(string iface, IReadOnlyDictionary<string, string> types)
        {
            var rows = new List<string>();
            ExpandRows(Unpack(iface), "", types, new HashSet<string>(StringComparer.Ordinal), rows);
            return OrderedInterface(rows);
        }
        private static void ExpandRows(IEnumerable<string> source, string prefix, IReadOnlyDictionary<string, string> types, HashSet<string> visiting, List<string> rows)
        {
            foreach (var row in source)
            {
                int colon = row.IndexOf(':'); if (colon < 0) throw new InvalidDataException("Malformed independent interface.");
                var path = row.Substring(0, colon); var type = row.Substring(colon + 1);
                if (prefix.Length != 0) path = prefix + path.Substring(path.IndexOf('/'));
                rows.Add(path + ":" + type);
                var tokens = Unpack(type);
                if (tokens.Count == 1 && tokens[0].StartsWith("\"", StringComparison.Ordinal))
                {
                    var name = tokens[0].Substring(1, tokens[0].Length - 2);
                    if (!types.TryGetValue(name, out var declaration) || !visiting.Add(name)) throw new InvalidDataException("Missing or cyclic independent UDT declaration: " + name);
                    ExpandRows(Unpack(declaration), path, types, visiting, rows); visiting.Remove(name);
                }
            }
        }
        private static List<string> Unpack(string packed)
        {
            var values = new List<string>();
            for (int i = 0; i < packed.Length;)
            {
                int colon = packed.IndexOf(':', i);
                if (colon < 0 || !int.TryParse(packed.Substring(i, colon - i), out var length) || length < 0 || length > packed.Length - colon - 1)
                    throw new InvalidDataException("Malformed independent interface projection.");
                values.Add(packed.Substring(colon + 1, length)); i = colon + 1 + length;
            }
            return values;
        }
        private static string OrderedInterface(IEnumerable<string> rows)
        {
            var order = new[] { "Input", "Output", "InOut", "Static", "Temp", "Constant", "Return" };
            return Pack(rows.OrderBy(r => Array.IndexOf(order, r.Substring(0, r.IndexOf('/')))));
        }
        private static string Interface(List<Token> tokens, string kind, string name)
        {
            var rows = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Quoted) continue;
                if (tokens[i].Value == "BEGIN") break;
                if (kind == "UDT" && tokens[i].Value == "STRUCT") { i++; Members(tokens, ref i, "Static", "END_STRUCT", rows); }
                else if (Sections.TryGetValue(tokens[i].Value, out var section))
                {
                    i++;
                    if (i < tokens.Count && tokens[i].Value == "RETAIN") throw new NotSupportedException("RETAIN needs an independently qualified storage profile.");
                    if (i < tokens.Count && tokens[i].Value == "NON_RETAIN") i++;
                    Members(tokens, ref i, section, "END_VAR", rows);
                }
            }
            if (kind == "FC")
            {
                if (tokens.Count < 2 || tokens[0].Value != ":") throw new InvalidDataException("FC return declaration missing.");
                int i = 1; var type = new List<Token>();
                while (i < tokens.Count && (tokens[i].Quoted || (tokens[i].Value != "VERSION" && tokens[i].Value != "TITLE" && tokens[i].Value != "BEGIN" && !Sections.ContainsKey(tokens[i].Value))) && tokens[i].Value != "{") type.Add(tokens[i++]);
                if (type.Count == 0) throw new InvalidDataException("FC return type missing.");
                rows.Add("Return/RET_VAL:" + Pack(type.Select(t => t.Value)));
            }
            return OrderedInterface(rows);
        }
        private static void Members(List<Token> tokens, ref int i, string path, string end, List<string> rows)
        {
            while (i < tokens.Count && tokens[i].Value != end)
            {
                var name = Identifier(tokens[i++]).ToUpperInvariant();
                if (i >= tokens.Count || tokens[i++].Value != ":") throw new InvalidDataException("Only one name per SCL declaration is supported.");
                var type = new List<Token>();
                while (i < tokens.Count && tokens[i].Value != ";" && tokens[i].Value != ":=" && tokens[i].Value != "STRUCT") type.Add(tokens[i++]);
                if (i < tokens.Count && tokens[i].Value == "STRUCT")
                {
                    if (type.Count != 0) throw new InvalidDataException("Unsupported inline STRUCT declaration.");
                    rows.Add(path + "/" + name + ":" + Pack(new[] { "STRUCT" })); i++;
                    Members(tokens, ref i, path + "/" + name, "END_STRUCT", rows); i++;
                }
                else
                {
                    if (type.Count == 0) throw new InvalidDataException("Missing interface data type.");
                    rows.Add(path + "/" + name + ":" + Pack(type.Select(t => t.Value)));
                }
                if (i < tokens.Count && tokens[i].Value == ":=") throw new NotSupportedException("Declaration start values need an independently qualified storage profile.");
                while (i < tokens.Count && tokens[i].Value != ";") i++;
                if (i == tokens.Count) throw new InvalidDataException("Unterminated SCL declaration."); i++;
            }
            if (i == tokens.Count) throw new InvalidDataException("Missing interface terminator " + end);
        }
        private static string Instance(List<Token> tokens)
        {
            var begin = tokens.FindIndex(t => !t.Quoted && t.Value == "BEGIN");
            if (begin < 0) throw new InvalidDataException("DB BEGIN is missing.");
            if (tokens.Take(begin).Any(t => !t.Quoted && t.Value.StartsWith("VAR", StringComparison.Ordinal))) return null;
            if (begin == 0 || !tokens[begin - 1].Quoted || !tokens[begin - 1].Value.StartsWith("\"", StringComparison.Ordinal))
                throw new InvalidDataException("Instance DB requires a named FB immediately before BEGIN.");
            return Identifier(tokens[begin - 1]);
        }
        private static string Identifier(Token t)
        {
            var name = t.Value.StartsWith("\"", StringComparison.Ordinal) ? t.Value.Substring(1, t.Value.Length - 2) : t.Raw;
            if (!ProjectGenerationSpecLoader.Identifier(name)) throw new InvalidDataException("Unsupported SCL identifier: " + name);
            return name;
        }
        private static string Pack(IEnumerable<string> values) => string.Concat(values.Select(v => v.Length + ":" + v));
        private static List<Token> Lex(string text)
        {
            if (text == null) throw new InvalidDataException("Missing SCL text.");
            var result = new List<Token>();
            for (int i = 0; i < text.Length;)
            {
                if (char.IsWhiteSpace(text[i]) || text[i] == '\ufeff') { i++; continue; }
                if (i + 1 < text.Length && text.Substring(i, 2) == "//") { while (i < text.Length && text[i] != '\n') i++; continue; }
                if (i + 1 < text.Length && (text.Substring(i, 2) == "(*" || text.Substring(i, 2) == "/*"))
                {
                    var open = text.Substring(i, 2); var close = open == "(*" ? "*)" : "*/"; int depth = 1; i += 2;
                    while (depth > 0 && i + 1 < text.Length)
                    { if (text.Substring(i, 2) == open) { depth++; i += 2; } else if (text.Substring(i, 2) == close) { depth--; i += 2; } else i++; }
                    if (depth != 0) throw new InvalidDataException("Unterminated SCL comment."); continue;
                }
                var start = i; var c = text[i++]; bool quoted = c == '\'' || c == '"';
                if (quoted)
                {
                    bool closed = false;
                    while (i < text.Length)
                    {
                        var next = text[i++];
                        if (next == '$') { if (i == text.Length) break; i++; continue; }
                        if (next == c) { if (i < text.Length && text[i] == c) { i++; continue; } closed = true; break; }
                    }
                    if (!closed) throw new InvalidDataException("Unterminated SCL literal.");
                }
                else if (char.IsLetterOrDigit(c) || c == '_')
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                else if (i < text.Length && new[] { ":=", "=>", "<=", ">=", "<>", "..", "**" }.Contains(text.Substring(start, 2))) i++;
                var value = text.Substring(start, i - start);
                result.Add(new Token { Value = quoted ? value : value.ToUpperInvariant(), Raw = value, Quoted = quoted, Start = start, End = i });
            }
            return result;
        }
    }
}
