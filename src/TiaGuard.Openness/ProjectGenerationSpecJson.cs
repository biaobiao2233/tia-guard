using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TiaGuard.Openness
{
    // Deliberately narrow JSON grammar: v1 contains only objects/arrays/strings.
    // Reject duplicate keys, coerced scalars, comments, trailing commas/data.
    internal sealed class ProjectGenerationSpecJson
    {
        private readonly string text;
        private int position;
        private ProjectGenerationSpecJson(string value) { text = value; }
        internal static object Parse(string text)
        {
            var reader = new ProjectGenerationSpecJson(text);
            var value = reader.Value(0); reader.White();
            if (reader.position != text.Length) throw new InvalidDataException("Trailing JSON data.");
            return value;
        }
        private void White() { while (position < text.Length && " \r\n\t".IndexOf(text[position]) >= 0) position++; }
        private bool Take(char c) { White(); if (position < text.Length && text[position] == c) { position++; return true; } return false; }
        private void Need(char c) { if (!Take(c)) throw new InvalidDataException("Expected JSON '" + c + "'."); }
        private object Value(int depth)
        {
            if (depth > 64) throw new InvalidDataException("JSON nesting exceeds limit.");
            White();
            if (Take('{'))
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                if (Take('}')) return result;
                do { var key = String(); Need(':'); if (result.ContainsKey(key)) throw new InvalidDataException("Duplicate JSON field: " + key); result.Add(key, Value(depth + 1)); } while (Take(','));
                Need('}'); return result;
            }
            if (Take('['))
            {
                var result = new List<object>();
                if (Take(']')) return result;
                do { result.Add(Value(depth + 1)); } while (Take(','));
                Need(']'); return result;
            }
            return String();
        }
        private string String()
        {
            Need('"'); var result = new StringBuilder();
            while (position < text.Length)
            {
                var c = text[position++];
                if (c == '"')
                {
                    var value = result.ToString();
                    try { new UTF8Encoding(false, true).GetBytes(value); }
                    catch (EncoderFallbackException) { throw new InvalidDataException("Invalid JSON Unicode."); }
                    return value;
                }
                if (c < 32) throw new InvalidDataException("JSON control character.");
                if (c != '\\') { result.Append(c); continue; }
                if (position == text.Length) break;
                c = text[position++];
                switch (c)
                {
                    case '"': case '\\': case '/': result.Append(c); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (position + 4 > text.Length || !ushort.TryParse(text.Substring(position, 4),
                            System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var code))
                            throw new InvalidDataException("Invalid JSON Unicode escape.");
                        result.Append((char)code); position += 4; break;
                    default: throw new InvalidDataException("Invalid JSON escape.");
                }
            }
            throw new InvalidDataException("Unterminated JSON string.");
        }
    }
}
