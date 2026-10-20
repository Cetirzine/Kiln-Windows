using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kiln.Core;

public enum JsonKind { Null, Bool, Number, String, Array, Object }

/// 轻量 JSON 值类型，便于处理版本 JSON 中结构不固定的字段（arguments、rules 等）。
/// 对应原版的 JSON 枚举：下标访问、类型化取值、序列化回写。
public readonly struct Json : IEquatable<Json>
{
    private readonly JsonElement _e;
    private Json(JsonElement e) => _e = e;

    public static Json Null => default;

    public static Json Parse(ReadOnlyMemory<byte> utf8)
    {
        try
        {
            using var doc = JsonDocument.Parse(utf8);
            return new Json(doc.RootElement.Clone());
        }
        catch (JsonException ex)
        {
            throw new LauncherException($"JSON 解析失败：{ex.Message}");
        }
    }

    public static Json Parse(string text) => Parse(Encoding.UTF8.GetBytes(text));

    /// 由字典 / 数组构造（对应原版 JSON.object(...) / JSON.array(...)）
    public static Json Of(string? s) => s == null ? Null : Parse(System.Text.Json.JsonSerializer.Serialize(s));
    public static Json Of(bool b) => Parse(b ? "true" : "false");
    public static Json Of(double n) => Parse(n.ToString("R", CultureInfo.InvariantCulture));

    public static Json From(IReadOnlyDictionary<string, Json> obj)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var (k, v) in obj)
            {
                writer.WritePropertyName(k);
                WriteValue(writer, v);
            }
            writer.WriteEndObject();
        }
        return Parse(ms.ToArray());
    }

    public static Json From(IReadOnlyList<Json> arr)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartArray();
            foreach (var v in arr) WriteValue(writer, v);
            writer.WriteEndArray();
        }
        return Parse(ms.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, Json v)
    {
        switch (v.Kind)
        {
            case JsonKind.Null: writer.WriteNullValue(); break;
            case JsonKind.Bool: writer.WriteBooleanValue(v.Bool!.Value); break;
            case JsonKind.Number: writer.WriteNumberValue(v.Double!.Value); break;
            case JsonKind.String: writer.WriteStringValue(v.String); break;
            case JsonKind.Array:
                writer.WriteStartArray();
                foreach (var e in v.Array) WriteValue(writer, e);
                writer.WriteEndArray();
                break;
            case JsonKind.Object:
                writer.WriteStartObject();
                foreach (var (k, e) in v.Object)
                {
                    writer.WritePropertyName(k);
                    WriteValue(writer, e);
                }
                writer.WriteEndObject();
                break;
        }
    }

    public static Json? TryParse(ReadOnlyMemory<byte> utf8)
    {
        try { return Parse(utf8); }
        catch { return null; }
    }

    public JsonKind Kind => _e.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => JsonKind.Bool,
        JsonValueKind.Number => JsonKind.Number,
        JsonValueKind.String => JsonKind.String,
        JsonValueKind.Array => JsonKind.Array,
        JsonValueKind.Object => JsonKind.Object,
        _ => JsonKind.Null,
    };

    public Json this[string key]
    {
        get
        {
            if (_e.ValueKind == JsonValueKind.Object && _e.TryGetProperty(key, out var v))
                return new Json(v);
            return Null;
        }
    }

    public Json this[int index]
    {
        get
        {
            if (_e.ValueKind == JsonValueKind.Array && index >= 0 && index < _e.GetArrayLength())
                return new Json(_e[index]);
            return Null;
        }
    }

    public string? String
    {
        get
        {
            switch (_e.ValueKind)
            {
                case JsonValueKind.String:
                    return _e.GetString();
                case JsonValueKind.Number:
                    double n = _e.GetDouble();
                    return n == Math.Round(n) && Math.Abs(n) < 1e15
                        ? ((long)n).ToString(CultureInfo.InvariantCulture)
                        : n.ToString("R", CultureInfo.InvariantCulture);
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return null;
            }
        }
    }

    public long? Int
    {
        get
        {
            if (_e.ValueKind == JsonValueKind.Number)
            {
                double d = _e.GetDouble();
                if (d >= long.MinValue && d <= long.MaxValue) return (long)d;
            }
            if (long.TryParse(String, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            return null;
        }
    }

    public double? Double => _e.ValueKind == JsonValueKind.Number ? _e.GetDouble() : null;

    public bool? Bool => _e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public bool IsNull => Kind == JsonKind.Null;
    public bool Exists => !IsNull;

    public IReadOnlyList<Json> Array
    {
        get
        {
            if (_e.ValueKind != JsonValueKind.Array) return System.Array.Empty<Json>();
            return _e.EnumerateArray().Select(e => new Json(e)).ToArray();
        }
    }

    public IReadOnlyDictionary<string, Json> Object
    {
        get
        {
            if (_e.ValueKind != JsonValueKind.Object) return new Dictionary<string, Json>();
            return _e.EnumerateObject().ToDictionary(p => p.Name, p => new Json(p.Value));
        }
    }

    public byte[] Data(bool pretty = true)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = pretty }))
        {
            if (_e.ValueKind == JsonValueKind.Undefined) writer.WriteNullValue();
            else _e.WriteTo(writer);
        }
        return ms.ToArray();
    }

    public bool Equals(Json other) =>
        _e.ValueKind == other._e.ValueKind
        && (_e.ValueKind == JsonValueKind.Undefined || JsonElement.DeepEquals(_e, other._e));

    public override bool Equals(object? obj) => obj is Json j && Equals(j);

    public override int GetHashCode() =>
        _e.ValueKind == JsonValueKind.Undefined ? 0 : _e.GetRawText().GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Encoding.UTF8.GetString(Data(false));
}
