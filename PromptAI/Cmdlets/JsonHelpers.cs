using System.Collections;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Minimal recursive serializer for PowerShell Hashtables / arrays / primitives
/// to JSON. Used for -Schema (the user-supplied JSON schema is most ergonomic
/// to construct as @{ ... } in PowerShell).
/// </summary>
internal static class JsonHelpers
{
    public static string SerializeHashtable(Hashtable h)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            WriteHashtable(w, h);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static void WriteHashtable(Utf8JsonWriter w, Hashtable h)
    {
        w.WriteStartObject();
        foreach (DictionaryEntry e in h)
        {
            var key = e.Key?.ToString() ?? throw new InvalidOperationException("Hashtable key cannot be null in JSON.");
            w.WritePropertyName(key);
            WriteValue(w, e.Value);
        }
        w.WriteEndObject();
    }

    public static void WriteValue(Utf8JsonWriter w, object? v)
    {
        switch (v)
        {
            case null:                 w.WriteNullValue(); break;
            case Hashtable h:          WriteHashtable(w, h); break;
            case IDictionary d:
                w.WriteStartObject();
                foreach (DictionaryEntry e in d)
                {
                    w.WritePropertyName(e.Key?.ToString() ?? throw new InvalidOperationException("Dictionary key cannot be null."));
                    WriteValue(w, e.Value);
                }
                w.WriteEndObject();
                break;
            case string s:             w.WriteStringValue(s); break;
            case bool b:               w.WriteBooleanValue(b); break;
            case int i:                w.WriteNumberValue(i); break;
            case long l:               w.WriteNumberValue(l); break;
            case double d:             w.WriteNumberValue(d); break;
            case decimal dec:          w.WriteNumberValue(dec); break;
            case System.Management.Automation.PSObject pso:
                WriteValue(w, pso.BaseObject);
                break;
            case IEnumerable list when v is not string:
                w.WriteStartArray();
                foreach (var item in list) WriteValue(w, item);
                w.WriteEndArray();
                break;
            default:                   w.WriteStringValue(v.ToString()); break;
        }
    }
}
