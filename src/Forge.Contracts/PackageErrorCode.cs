namespace Forge.Contracts;

// Error codes from SQL.md, Error Conditions, named exactly as the document
// names them so the CLI can print them verbatim. Only the codes the
// implemented slice can produce are present; dependency-resolution,
// conflict, and removal codes arrive with their work items, and
// E_DEPENDENCY_UNSATISFIED covers catalog name resolution until dependency
// resolution arrives.
public enum PackageErrorCode
{
    E_MANIFEST_INVALID,
    E_MIGRATION_NAME_INVALID,
    E_MIGRATION_DUPLICATE_PREFIX,
    E_TARGET_MISMATCH,
    E_ALREADY_INSTALLED,
    E_INSTALL_INTERRUPTED,
    E_NOT_INSTALLED,
    E_DOWNGRADE_REFUSED,
    E_MIGRATION_MODIFIED,
    E_MIGRATION_FAILED,
    E_DEPENDENCY_UNSATISFIED,
}
