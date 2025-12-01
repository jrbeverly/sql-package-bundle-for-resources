namespace Forge.Contracts;

// Error codes the realm layer produces, named after the failures they
// describe (DEPLOY.md, Error Conditions, adapted to the PoC cuts). Package
// and catalog failures keep their own codes; the realm layer wraps a package
// failure only where the deploy sequence itself is reporting it.
public enum RealmErrorCode
{
    E_BUNDLE_INVALID,
    E_TARGET_MISMATCH,
    E_PACKAGE_UNRESOLVED,
    E_PACKAGE_INSTALL_FAILED,
    E_REALM_NOT_DEPLOYED,
    E_PROVISION_FAILED,
    E_CONFIG_KEY_MISSING,
    E_CONFIG_FORMAT_UNEXPECTED,
    E_REGISTRATION_FAILED,
    E_CONTAINER_FAILED,
    E_STATE_FAILED,
}
