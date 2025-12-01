# Vision

## Desired Outcome

Database content for application deployments becomes a distributable,
verifiable, installable artifact, and an environment becomes a deployable description
rather than a hand-configured machine.

An operator should be able to trust a creator once, discover what that creator
publishes, install a content package into a primary database, upgrade it later, and
stand up a whole environment from a bundle — without a central registry, without
modifying the core, and without writing anything other than standard SQL.

---

# The Three Layers

The system is three layers that each work alone and compose upward.

```
Layer 3   Environment          a deployable environment built from packages
             ▲
Layer 2   Package        versioned SQL content with tracked installation
             ▲
Layer 1   Catalog        signed discovery of immutable artifacts
```

Each layer is usable without the one above it. A package installs from a local
directory with no catalog involved. A catalog distributes any immutable bytes,
not only SQL packages. The layering is a dependency direction, not a required
workflow.

---

# Catalog Vision

Identity belongs to a key, not to a host.

A publisher is a public key with a human-readable label. A catalog is a signed
JSON document listing artifacts and recommending other catalogs. Anyone can
publish by hosting one static file and signing it. Moving hosts does not change
who you are.

The catalog layer establishes:

- **Identity** from cryptographic keys, so a creator survives changing hosts.
- **Integrity** from digests, so bytes are verified independently of where they
  came from.
- **Discovery** by recommendation between catalogs, so an ecosystem can form
  without an owner.
- **Trust** as an explicit local decision, so discovery never enrols a publisher
  on the operator's behalf.

Trust does not propagate. A trusted publisher recommending another publisher
produces a suggestion shown to the operator, never an enrolment. This is the
property that makes decentralised discovery safe enough to be worth having.

---

# Package Vision

Content becomes a named, versioned thing with a boundary.

A package is a directory holding a manifest and ordered SQL migrations. The
manifest says what the package is, what it targets, and what it needs. The
migrations say what it does, in standard SQL, reviewable one file at a time.

The package layer establishes:

- **Identity and version** for a unit of content, so it can be referred to,
  depended on, and upgraded.
- **Installation state** recorded in the target database, so the database can
  answer what is installed and at which version.
- **Deterministic ordering**, so the same package applied to the same starting
  database produces the same result.
- **Dependency and conflict declaration**, so packages that need each other
  install together and packages that cannot coexist fail early instead of
  corrupting a primary.

The package format is a wrapper, not a replacement. The SQL inside it is the
same SQL an operator would have applied by hand.

---

# Environment Vision

An environment becomes a description that can be deployed, rebuilt, and thrown away.

An environment bundle names a primary database's content, the configuration that runs it,
and the identity it registers under. Deploying it provisions the databases,
applies the content, writes the configuration, registers the environment with the
shared authentication service, and starts the server.

The environment layer establishes:

- **Reproducibility**: the same bundle deploys the same environment.
- **Isolation**: each environment owns its own primary and analytics databases, so one
  environment's content cannot affect another's.
- **Multiplicity**: several environments coexist behind one authentication service,
  differing only in configuration.
- **Disposability**: an environment can be deleted and redeployed, which is what makes
  experimentation affordable.

An operator interacts with environments. Containers, connection strings, and SQL are
implementation detail the tooling owns.

---

# What Success Looks Like

The experiment has answered its question when an operator can, from a clean
machine:

1. Trust a publisher by key after being shown who they are.
2. Discover a SQL package through that publisher's signed catalog.
3. Install the package into a primary database and see it recorded there.
4. Upgrade it to a later version without reimporting the database.
5. Deploy a second environment from a bundle that references the same package, running
   alongside the first, sharing one authentication service.

Every step verifiable, none of it requiring a registry, and no line of the
target application changed.

---

# Design Philosophy

Separate the concerns that are usually welded together.

- **Identity** is a key.
- **Discovery** is a recommendation.
- **Trust** is a local decision.
- **Distribution** is any transport that serves immutable bytes.
- **Verification** is signatures over catalogs and digests over artifacts.
- **Installation** is standard SQL applied in a recorded order.
- **Deployment** is configuration generated from a description.

Each of these is independently replaceable. Welding any two together — trust to
discovery, identity to hosting, installation to distribution — is what makes
existing approaches either centralised or unsafe.

The system extends existing application workflows rather than competing with them. A
server that never installs a package is unaffected; a package that is never
published in a catalog still installs; a catalog that never carries SQL still
works. Nothing here is load-bearing for anything else.
