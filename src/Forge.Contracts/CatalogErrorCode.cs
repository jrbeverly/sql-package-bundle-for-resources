namespace Forge.Contracts;

// Error codes from SPEC.md, Error Conditions, named exactly as the document
// names them so the CLI can print them verbatim. Only the codes the
// implemented slice can produce are present; expiry, rollback, key-change, and
// the remaining download codes arrive with their work items.
public enum CatalogErrorCode
{
    E_SPEC_VERSION_UNSUPPORTED,
    E_CANONICALIZATION_FAILED,
    E_SIGNATURE_INVALID,
    E_PUBLISHER_UNTRUSTED,
    E_DIGEST_MISMATCH,
}
