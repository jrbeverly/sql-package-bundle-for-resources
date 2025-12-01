using Forge.Contracts;

namespace Forge.Environments;

// The outcome of a realm deployment: the recorded state plus notes for the
// operator, such as a package already installed by an earlier deploy.
public sealed record DeployedRealm(RealmState State, IReadOnlyList<string> Notes);
