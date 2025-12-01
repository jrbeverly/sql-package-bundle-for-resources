using System.Globalization;

namespace Forge.Catalog;

// RFC 3339 timestamps in the UTC "Z" form SPEC.md requires. Parsing stays
// lenient (offsets, fractional seconds) so catalogs from other tools read.
internal static class JsonTime
{
    public static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static bool TryParse(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
}
