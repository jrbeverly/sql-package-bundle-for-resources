using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Forge.Catalog;

// The JSON Canonicalization Scheme (RFC 8785), implemented against the RFC
// and its published test vectors (HLD.md, Phase 2 work item 01). Signatures
// cover the canonical serialization of a catalog, so any deviation here
// invalidates every signature this code produces or checks.
public static class Jcs
{
    public static string Canonicalize(JsonNode node)
    {
        var builder = new StringBuilder();
        Write(node, builder);
        return builder.ToString();
    }

    private static void Write(JsonNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject obj:
                WriteObject(obj, builder);
                break;
            case JsonArray array:
                WriteArray(array, builder);
                break;
            case JsonValue value:
                WriteValue(value, builder);
                break;
        }
    }

    // RFC 8785 3.2.3: property names sort by UTF-16 code units, as unsigned
    // integers, independent of locale; Ordinal compares exactly that.
    private static void WriteObject(JsonObject obj, StringBuilder builder)
    {
        builder.Append('{');
        var first = true;
        foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            WriteString(property.Key, builder);
            builder.Append(':');
            Write(property.Value, builder);
        }

        builder.Append('}');
    }

    private static void WriteArray(JsonArray array, StringBuilder builder)
    {
        builder.Append('[');
        var first = true;
        foreach (var element in array)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            Write(element, builder);
        }

        builder.Append(']');
    }

    private static void WriteValue(JsonValue value, StringBuilder builder)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.String:
                WriteString(value.GetValue<string>(), builder);
                break;
            case JsonValueKind.Number:
                // ToJsonString preserves the raw text of a parsed number and
                // emits the invariant spelling of a programmatic one; either
                // way it is the document's own number text.
                builder.Append(SerializeNumber(value.ToJsonString()));
                break;
            default:
                throw new CanonicalizationException($"Unsupported JSON value kind {value.GetValueKind()}.");
        }
    }

    // RFC 8785 3.2.2.2: control characters U+0000-U+001F use lowercase hex
    // escapes, except the five predefined ones; every other code point is
    // emitted as is, except " and \. Lone surrogates must fail.
    private static void WriteString(string value, StringBuilder builder)
    {
        builder.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"')
            {
                builder.Append("\\\"");
            }
            else if (c == '\\')
            {
                builder.Append("\\\\");
            }
            else if (c < 0x20)
            {
                builder.Append(c switch
                {
                    '\b' => "\\b",
                    '\t' => "\\t",
                    '\n' => "\\n",
                    '\f' => "\\f",
                    '\r' => "\\r",
                    _ => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                });
            }
            else if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    throw new CanonicalizationException("String contains a lone surrogate, which RFC 8785 forbids.");
                }

                builder.Append(c).Append(value[i + 1]);
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                throw new CanonicalizationException("String contains a lone surrogate, which RFC 8785 forbids.");
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append('"');
    }

    // RFC 8785 3.2.2.3: serialize per ECMAScript 7.1.12.1, over the IEEE 754
    // double the number text denotes. .NET's "R" format is Ryu, which the RFC
    // names as a compatible reference implementation, and yields the shortest
    // round-trippable digits; the ES6 steps below reformat them.
    private static string SerializeNumber(string rawText)
    {
        if (!double.TryParse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            // Covers 1e999: not a finite double, and NaN/Infinity are not JSON.
            throw new CanonicalizationException($"Number '{rawText}' is not a finite IEEE 754 double.");
        }

        if (value == 0)
        {
            return "0"; // ES6 step 2: negative zero serializes as 0.
        }

        var negative = value < 0;
        if (negative)
        {
            value = -value;
        }

        var (digits, exponent) = ShortestDigits(value);
        var k = digits.Length;
        var n = exponent;

        string serialized;
        if (k <= n && n <= 21)
        {
            serialized = digits + new string('0', n - k);
        }
        else if (0 < n && n <= 21)
        {
            serialized = digits[..n] + "." + digits[n..];
        }
        else if (-6 < n && n <= 0)
        {
            serialized = "0." + new string('0', -n) + digits;
        }
        else
        {
            var mantissa = k == 1 ? digits : digits[..1] + "." + digits[1..];
            var e = n - 1;
            serialized = $"{mantissa}e{(e < 0 ? "-" : "+")}{Math.Abs(e).ToString(CultureInfo.InvariantCulture)}";
        }

        return negative ? "-" + serialized : serialized;
    }

    // Shortest round-trippable digits and their decimal exponent, such that
    // value = digits * 10^(exponent - digits.Length).
    private static (string Digits, int Exponent) ShortestDigits(double value)
    {
        var r = value.ToString("R", CultureInfo.InvariantCulture);
        var mantissa = r;
        var exponent = 0;
        var eIndex = r.IndexOf('E');
        if (eIndex >= 0)
        {
            mantissa = r[..eIndex];
            exponent = int.Parse(r[(eIndex + 1)..], CultureInfo.InvariantCulture);
        }

        var point = mantissa.IndexOf('.');
        var pointPosition = point >= 0 ? point : mantissa.Length;
        var digits = point >= 0 ? mantissa.Remove(point, 1) : mantissa;
        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits[leadingZeros..];
        return (digits, exponent + pointPosition - leadingZeros);
    }
}
