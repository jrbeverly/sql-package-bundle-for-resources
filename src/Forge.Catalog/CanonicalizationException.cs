namespace Forge.Catalog;

// Input that RFC 8785 forbids: lone surrogates in strings, or a number that is
// not a finite IEEE 754 double. The verifier maps this to
// E_CANONICALIZATION_FAILED; it never crosses the catalog boundary.
internal sealed class CanonicalizationException(string message) : Exception(message);
