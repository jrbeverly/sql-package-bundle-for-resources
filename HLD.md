# High-Level Design

**Status:** Decisions are binding. Where this document and a format document
disagree, this document wins.

## Purpose

[PROBLEM.md](PROBLEM.md) and [VISION.md](VISION.md) say why. [SPEC.md](SPEC.md),
[SQL.md](SQL.md), and [DEPLOY.md](DEPLOY.md) say what the formats are. This
document says **what gets built, in what order, with what, and when each part is
done**.

Everything here is decided. An implementer should not need to choose a library, a
project layout, a naming scheme, or a phase boundary — and should not invent an
alternative to one stated here. Where a choice is genuinely open it is marked
*open*, and there are currently none blocking Phase 1.

---

# Decision Summary

| Decision | Choice | Rationale |
| -------- | ------ | --------- |
| Language and runtime | C# on .NET 8, target framework `net8.0` | SDK 8.0.424 is present in the devcontainer. Long-term-support runtime, and the monorepo already has a .NET reference in `ai-driven/mcp-server-private-dataset`. |
| SDK pin | `global.json`, version `8.0.424`, `rollForward: latestFeature` | 9.0.317 is also installed; without a pin the build silently moves. |
| Distribution shape | One CLI executable, `forge`, three verb groups | Three layers, one tool. Separate binaries would duplicate configuration, trust-store access, and error handling three times. |
| CLI framework | `System.CommandLine` | Verb groups, parsing, and help for free. Accept that it is pre-release; it is the only first-party option. |
| Manifest format | YAML, via `YamlDotNet` | Manifests are hand-written and reviewed; YAML comments matter. |
| Catalog format | JSON, via `System.Text.Json` | Catalogs are signed and machine-produced; JSON has a canonicalisation standard and YAML does not. |
| Canonical JSON | RFC 8785 (JCS), implemented in `Forge.Catalog` | No maintained .NET JCS library worth the dependency. It is ~200 lines, fully specified, and has published test vectors. |
| Signatures | Ed25519 via `BouncyCastle.Cryptography` | .NET 8's `System.Security.Cryptography` has no Ed25519. BouncyCastle is pure managed; `NSec` was rejected because it carries a native libsodium dependency into every container. |
| Digests | `System.Security.Cryptography.SHA256` | In-box. |
| Database access | `MySqlConnector` | MIT, async-first, and supports multi-statement batches, which [SQL.md](SQL.md) requires. `MySql.Data` was rejected on licence and API age. |
| Migration execution | Whole file as one multi-statement batch | Client-side statement splitting needs a SQL lexer; a partial one corrupts the content being distributed. |
| Containers | Generated `compose.yaml`, driven by the `docker compose` CLI | Keeps a deployment reproducible by hand. `Docker.DotNet` was rejected as an opaque coupling. |
| Test framework | xUnit | Matches the monorepo's other .NET work. |
| Integration tests | Marked `[Trait("Category", "Integration")]` and excluded by `make test`; they need `FORGE_TEST_MYSQL` when run | Unit tests must run with no services. `Testcontainers` was rejected: it needs Docker-in-Docker inside a devcontainer that already drives Docker. |
| Error handling | An error-code enum per layer, mapped to exit codes at the CLI boundary | The format documents already enumerate every failure. Exceptions do not cross into `Forge.Cli`. |
| Configuration | Environment variables and a single config file; no config discovery magic | See [TECHNICAL.md](TECHNICAL.md). |

---

# What We Are Building

A single CLI over three layers, built in three phases.

```
forge package   Layer 2   SQL.md      Phase 1
forge catalog   Layer 1   SPEC.md     Phase 2
forge environment     Layer 3   DEPLOY.md   Phase 3
```

Phase order is deliberately not layer order. Layer 2 is built first because it
delivers the whole point of the system — installable, tracked, upgradeable
content — with no cryptography and no containers. Layer 1 then makes those
packages distributable, and Layer 3 composes both into an environment.

Each phase ends with something an operator can use. No phase exists only to
prepare for the next.

## In Scope

- `manifest.yaml` parsing and validation, SemVer, the constraint grammar
- Migration discovery, ordering, content validation, batch execution
- Installation tracking tables, install / upgrade / remove / list
- Dependency resolution and conflict detection
- RFC 8785 canonicalisation, Ed25519 signing and verification
- Catalog publishing, verification, discovery walk with limits, trust store
- Artifact download over HTTPS with digest verification
- Environment bundles, database provisioning, config override, `forge_environment`
  registration, container lifecycle, backup, delete

## Out Of Scope For V1

Stated so an implementer does not build them speculatively:

| Excluded | Note |
| -------- | ---- |
| OCI transport for artifact sources | [SPEC.md](SPEC.md) permits a client to skip source types it does not implement. V1 implements `https` only and skips `oci` entries. |
| Community file import | The format is specified; the import command is not built in V1. |
| Restore from backup | `backup` produces dumps; restoring them is the operator's `mysql` invocation. |
| Per-package signatures | Integrity comes from the catalog's signature over the artifact digest. A second signing scheme inside the package adds no property V1 needs. |
| Downgrade | Refused with `E_DOWNGRADE_REFUSED`. A downgrade is `remove` then `install`, stated by the operator. |
| Automatic recovery from a failed migration | Forward-only by design. See Failure Semantics in [SQL.md](SQL.md). |
| A GUI, a web UI, or a daemon | The CLI is the whole interface. |
| Multi-operator access control, tenancy, hosted service | One operator, their own machines. |
| Windows support | The devcontainer is Linux; nothing may depend on Windows and nothing need work there. |
| Core detection | `spec.targets.core` is compared for string equality against configured value. Never inferred. |

---

# Architecture

```
                         Forge.Cli
                 System.CommandLine wiring only
                             │
        ┌────────────────────┼────────────────────┐
        │                    │                    │
   Forge.Catalog       Forge.Packaging       Forge.Environments
   Layer 1             Layer 2               Layer 3
   JCS, Ed25519,       manifests,            bundles, config
   catalogs, trust,    migrations,           override, compose,
   discovery, fetch    resolution,           forge_environment, state
        │              package hash               │
        │                    │                    │
        │                    └─── Forge.Database ─┤
        │                         MySQL execution │
        │                         tracking tables │
        │                                         │
        └──────────── Forge.Contracts ────────────┘
                   records, enums, error codes
```

## Project Dependency Rules

These are invariants, not preferences. A test asserts them.

| Rule | Reason |
| ---- | ------ |
| `Forge.Contracts` depends on nothing but the BCL | It is the shared vocabulary; a dependency here reaches everywhere. |
| `Forge.Catalog` MUST NOT reference `Forge.Packaging`, `Forge.Database`, or `Forge.Environments` | The catalog layer is artifact-agnostic. If it can see a SQL package, it will eventually special-case one, and [SPEC.md](SPEC.md)'s separation is gone. |
| `Forge.Packaging` MUST NOT reference `Forge.Database` | Parsing, validation, and resolution are pure and testable without a server. The database is where the results are applied, not how they are computed. |
| `Forge.Environments` may reference `Forge.Packaging` and `Forge.Database` | Deployment composes them. |
| Nothing references `Forge.Cli` | It is the entry point. |
| No project references a test project | — |

The important one is the second. It is the architectural claim the whole design
rests on: distribution does not know what it distributes.

## Solution Layout

```
sql-package-bundle-for-resources/
├── global.json
├── Forge.sln
├── Makefile
├── src/
│   ├── Forge.Contracts/
│   ├── Forge.Packaging/
│   ├── Forge.Database/
│   ├── Forge.Catalog/
│   ├── Forge.Environments/
│   └── Forge.Cli/
├── tests/
│   ├── Forge.Packaging.Tests/
│   ├── Forge.Database.Tests/
│   ├── Forge.Catalog.Tests/
│   ├── Forge.Environments.Tests/
│   ├── Forge.Architecture.Tests/
│   └── Forge.EndToEnd.Tests/
├── fixtures/
│   ├── packages/
│   ├── catalogs/
│   └── environments/
└── ops/docker/docker-compose.yml
```

One file per public type. `fixtures/` holds the packages, catalogs, and bundles
the tests read; they are inputs, not generated output.

---

# Major Components

## Forge.Contracts

Records, enums, and error codes shared between layers. No behaviour beyond
validation that belongs to the type itself.

Holds: `PackageManifest`, `EnvironmentBundle`, `Catalog`, `Artifact`, `Publisher`,
`SemanticVersion`, `VersionConstraint`, `Digest`, the error-code enums, and
`Result<T>`.

Errors are values. `Result<T>` carries either a value or an error code plus a
message; the layers return it and `Forge.Cli` maps it to an exit code.

## Forge.Packaging

Everything about a package that does not touch a database.

- Read and validate `manifest.yaml`, rejecting unknown fields
- Parse and compare SemVer; parse and evaluate the constraint grammar
- Discover migrations, validate names, detect duplicate prefixes, order
  numerically
- Scan migration content for forbidden statements, reporting file and line
- Compute the package hash
- Resolve dependencies into an ordered install plan; detect cycles and
  unsatisfiable constraints
- Detect declared conflicts in both directions

Pure. Filesystem access goes through an interface so tests use in-memory
fixtures. This project has the highest test density in the solution because every
rule in [SQL.md](SQL.md) is checkable here without a server.

## Forge.Database

The only project that opens a MySQL connection.

- Create the tracking tables
- Read and write `forge_installed_package` and `forge_applied_migration`
- Execute a migration file as one batch inside a transaction
- Record each migration with its digest and duration
- Provision and drop databases (used by Layer 3)
- Read `information_schema` for the `forge_environment` column check
- Write the `forge_environment` row

It executes a plan; it does not build one. Ordering, validation, and resolution
happened in `Forge.Packaging`.

## Forge.Catalog

The OAC client and publisher, with no knowledge of SQL.

- RFC 8785 canonicalisation
- Ed25519 keypair generation, signing, verification
- Catalog serialisation preserving unknown fields
- The verification pipeline in order: spec version, canonicalise, signature,
  expiry, rollback, trust
- The trust store: publishers keyed by public key, highest version accepted, key
  change detection
- Discovery walk with cycle detection and every limit in [SPEC.md](SPEC.md),
  enforced while streaming
- HTTPS fetch with scheme re-check after each redirect, size cap during read,
  and digest verification of downloaded bytes

Unknown-field preservation is a correctness requirement, not a nicety: unknown
fields are covered by the signature, so dropping one makes a valid catalog look
tampered with.

## Forge.Environments

Environment deployment, composing the layers below it.

- Read and validate an environment bundle
- Derive database, container, and directory names from `metadata.name`
- Allocate and record the application-server port
- Override the config template's six keys, preserving everything else
- Generate `compose.yaml` and drive `docker compose`
- Own `state.json` and the deployment directory
- Orchestrate the deploy sequence in [DEPLOY.md](DEPLOY.md)

## Forge.Cli

Verb wiring and nothing else: parse arguments, call a layer, map `Result<T>` to
an exit code, render output. No validation, no orchestration, no SQL. A rule of
thumb that holds: if a method in `Forge.Cli` has a branch that is not about
presentation, it is in the wrong project.

---

# Phases

Each phase lists work items in build order. A work item is one reviewable change
that leaves the build green. The acceptance criteria are what "done" means; they
are verifiable, and a phase is not complete while one is unmet.

## Phase 1 — SQL Packages

Normative document: [SQL.md](SQL.md).

| # | Work item |
| - | --------- |
| 01 | Solution skeleton: `global.json`, `Forge.sln`, six `src` projects, test projects, `Makefile`, `.editorconfig`, `Directory.Build.props` with nullable and warnings-as-errors |
| 02 | `SemanticVersion` and `VersionConstraint`: parsing, comparison, SemVer §11 pre-release exclusion, rejection of `^` and `~` |
| 03 | `PackageManifest` reading and validation, unknown-field rejection, field rules |
| 04 | Migration discovery: name validation, numeric ordering, duplicate-prefix detection |
| 05 | Migration content validation: forbidden statements, reported with file and line |
| 06 | Package hash, with the NUL-separated encoding and ordinal path sort |
| 07 | Dependency resolution: graph, cycle detection, highest-satisfying selection, topological order, conflict checks both directions |
| 08 | Tracking tables: DDL, create-if-absent, read and write both tables |
| 09 | Migration execution: one batch per file, per-migration transaction, digest and duration recorded |
| 10 | `forge package install`, including the dependency plan printed before execution |
| 11 | `forge package upgrade`: version comparison, downgrade refusal, recorded-digest check, apply only unrecorded migrations |
| 12 | `forge package remove`: rollback coverage check, dependant check, reverse-order execution |
| 13 | `forge package list` |
| 14 | Failure semantics: `state = 'failed'`, interrupted-install detection, stop-on-first-failure, the reporting in [SQL.md](SQL.md) |
| 15 | `fixtures/packages/`: a valid package, one per validation failure, a dependency chain, a cycle, a conflicting pair, a package with `rollback/` |
| 16 | End-to-end test: install, upgrade, list, remove against `FORGE_TEST_MYSQL` |

**Acceptance criteria**

- `make build` and `make test` pass with no MySQL available; `make test` excludes
  integration tests by trait filter rather than skipping them at runtime.
- With `FORGE_TEST_MYSQL` set, `forge package install` applies a fixture package
  and both tracking tables reflect it.
- Every error code in [SQL.md](SQL.md) has a test that provokes it and asserts
  the code and exit status.
- A package hash is stable across re-archiving and changes when any file changes.
- A migration containing `DELIMITER` is rejected before any statement executes.
- An `install` interrupted after migration 2 of 4, re-run, applies 3 and 4 only.
- A dependency cycle is reported naming the cycle, with nothing written.
- `Forge.Packaging` has no reference to `Forge.Database`, asserted by
  `Forge.Architecture.Tests`.

## Phase 2 — Catalogs

Normative document: [SPEC.md](SPEC.md).

| # | Work item |
| - | --------- |
| 01 | RFC 8785 canonicalisation, against the published JCS test vectors |
| 02 | Ed25519 keypair generation, sign, verify; base64 encoding of keys and signatures |
| 03 | Catalog model and serialisation with unknown-field preservation |
| 04 | Signing: remove `signature`, canonicalise, sign, reinsert |
| 05 | The verification pipeline in order, each step its own failure code |
| 06 | Trust store: keyed by public key, display name, highest version accepted |
| 07 | Key-change detection and the refusal path |
| 08 | Rollback protection against the recorded highest version |
| 09 | HTTPS fetch: scheme re-check after redirect, streaming size cap, timeout, redirect cap |
| 10 | Artifact download and digest verification, discarding bytes on mismatch |
| 11 | Discovery walk: recursion, cycle detection, every limit, no partial enrolment |
| 12 | Catalog publishing: build from a directory, carry recommendations forward, increment `version`, sign, then verify the output through the client path |
| 13 | Distribution binding from [SQL.md](SQL.md): archive extraction rejecting absolute paths, `..` segments, and symlinks; manifest-versus-artifact identity check; name resolution restricted to the SQL-package media type |
| 14 | `forge catalog publish`, `verify`, `add`, `trust`, `untrust`, `list`, `discover`, `fetch`; `forge package search` |
| 15 | `fixtures/catalogs/`: valid, bad signature, expired, rolled-back, changed key, cyclic pair, oversized, `http` source, unknown fields, an archive with a `..` entry |
| 16 | End-to-end test: publish a catalog over a fixture package, verify it, trust the publisher, download and install |

**Acceptance criteria**

- JCS implementation passes the RFC 8785 test vectors.
- A catalog round-tripped through read and re-serialise still verifies, with
  unknown fields intact.
- Every error code in [SPEC.md](SPEC.md) has a test provoking it.
- An untrusted publisher's artifact cannot be downloaded; the failure is
  `E_PUBLISHER_UNTRUSTED`.
- A catalog recommending a trusted publisher's catalog does not enrol the
  recommended publisher.
- A key change on a trusted publisher `id` is refused and both keys are shown.
- A 20 MB catalog is rejected against the 10 MB limit, asserted with a counting
  stream showing the fetch stopped reading shortly past the cap rather than
  buffering the whole document.
- `Forge.Catalog` has no reference to `Forge.Packaging`, asserted by
  `Forge.Architecture.Tests`.

## Phase 3 — Environments

Normative document: [DEPLOY.md](DEPLOY.md).

| # | Work item |
| - | --------- |
| 01 | `EnvironmentBundle` reading and validation, unknown-field rejection |
| 02 | Derived names and port allocation, recorded in state |
| 03 | Config override: the six keys, template preservation, missing-key and format failures |
| 04 | `compose.yaml` generation, read-only asset mounts |
| 05 | Database provisioning and the operator-supplied analytics schema |
| 06 | `forge_environment` schema inspection and the idempotent row write |
| 07 | `state.json`, the state machine, bundle hash |
| 08 | `forge environment deploy`, in the order given in [DEPLOY.md](DEPLOY.md), plan-before-write |
| 09 | `forge environment start`, `stop`, `list` |
| 10 | `forge environment backup`, including `forge package list` output |
| 11 | `forge environment delete`, with named confirmation and no `--force` bypass |
| 12 | `fixtures/environments/`: a bundle with a dump, one with packages, one with both, invalid ones, a template missing a key |
| 13 | End-to-end test: deploy two environments against one `identity`, confirm isolation |

**Acceptance criteria**

- A generated `application.conf` differs from its template only in the six keys,
  byte-for-byte elsewhere, comments included.
- A template missing `PrimaryServerPort` fails with `E_CONFIG_KEY_MISSING` and
  writes nothing.
- A `forge_environment` table missing a needed column fails with
  `E_REGISTRY_SCHEMA_UNEXPECTED`; the table is never altered.
- Deploying the same bundle twice updates one `forge_environment` row rather than
  inserting two.
- Two environments deployed from bundles carrying the same package have independent
  tracking tables; removing it from one leaves the other installed.
- `delete` without the named confirmation drops nothing.
- Client-asset mounts are read-only in the generated `compose.yaml`.

---

# Conventions

Binding on generated code. These exist because their absence has a predictable
shape, and reviewing it afterwards costs more than stating it now.

- Nullable reference types enabled, warnings as errors, in
  `Directory.Build.props`.
- One public type per file, named for the file.
- Records for data, classes for behaviour. No data classes with mutable public
  setters crossing a project boundary.
- No `partial` classes, no reflection-driven dispatch, no source generators, no
  dependency-injection container. Construct dependencies explicitly.
- `async` only where there is real I/O. No `async` wrappers over synchronous
  work, no `.Result`, no `.Wait()`.
- Errors are returned as `Result<T>`, not thrown, across project boundaries. An
  exception escaping into `Forge.Cli` is a bug, and the top-level handler reports
  it as one.
- Every error path carries the error code from the format document and a message
  naming the offending file, package, or field.
- No comment restating what the line does. Comment only a decision the code
  cannot express — a spec rule being satisfied, a MySQL behaviour being worked
  around.
- No logging framework. Write to stdout and stderr; `--verbose` raises detail.
- No retry logic, no circuit breakers, no caching beyond what
  [SPEC.md](SPEC.md) specifies.
- SQL that the tool itself issues uses parameters, never string concatenation.
  Migration content is the exception: it is a batch supplied by the operator and
  sent verbatim.
- Tests name the rule they check, not the method they call:
  `DuplicateMigrationPrefixIsRejected`, not `DiscoverMigrationsTest3`.

## Do Not Invent

Every item here has a right answer that lives outside this repository. Reading it
is the work; guessing it is a defect even when the guess compiles.

- application configuration keys beyond the six in [DEPLOY.md](DEPLOY.md). Read the
  template.
- `forge_environment` columns. Read `information_schema`.
- The analytics-database schema. The operator supplies it.
- Core identifiers. Compared as opaque strings, never parsed or inferred.
- The environment's advertised address. It comes from configuration; a guess from a
  local interface or the database host produces a joinable-looking environment that
  nobody can join.
- A source URL for a published artifact. The publisher supplies it; a local path
  is not a URL.
- Fields in any manifest, bundle, or catalog that its format document does not
  list.
- Error codes. The three format documents enumerate all of them; if a failure has
  no code, that is a documentation gap to report, not a code to make up.
- NuGet package versions beyond those in [TECHNICAL.md](TECHNICAL.md).

When a needed fact is absent from these documents, the implementation should stop
and report the gap rather than fill it. A wrong assumption here writes into an
operator's primary database.

---

# Risks

| Risk | Mitigation |
| ---- | ---------- |
| `System.CommandLine` is pre-release and its API has shifted between betas | Pin the exact version in [TECHNICAL.md](TECHNICAL.md); keep `Forge.Cli` thin enough that an API change is a rewrite of wiring, not of logic. |
| Partial DDL failure leaves a primary database in an undefined state | Forward-only recovery is specified, not worked around; migrations are recorded individually so a re-run resumes. |
| A `forge_environment` schema difference between cores corrupts the shared auth database | Inspect the schema and refuse surprises; never `ALTER`. |
| An RFC 8785 implementation that is subtly wrong makes every signature unverifiable against other implementations | Test against the published JCS vectors before anything depends on it. |
| Unknown-field loss breaks signatures in a way that looks like tampering | Round-trip test in Phase 2, work item 03, before signing is built on top. |
| Scope growth into a package manager for everything | The out-of-scope table is binding; `Forge.Catalog`'s dependency rule is asserted by a test. |
