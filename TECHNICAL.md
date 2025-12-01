# Technical

Implementation detail for the decisions in [HLD.md](HLD.md). Where this document
gives an exact value — a package version, an exit code, a command name — that
value is the one to use.

---

# Toolchain

| Item | Value |
| ---- | ----- |
| SDK | 8.0.424, pinned in `global.json` with `rollForward: latestFeature` |
| Target framework | `net8.0` |
| Language version | default for the SDK; do not set `<LangVersion>` |
| Solution | `Forge.sln` at the repository root |

`global.json`:

```json
{
  "sdk": {
    "version": "8.0.424",
    "rollForward": "latestFeature"
  }
}
```

`Directory.Build.props` applies to every project:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`InvariantGlobalization` is set because every comparison in this system is
ordinal — versions, paths, digests, base64 — and a culture-sensitive comparison
sneaking into one of them is a class of bug this removes outright.

---

# Dependencies

Known-good at authoring time. Pin these exact versions in the `.csproj` files.
Roll forward only within the same major version, and only with a reason.

| Package | Version | Used by | For |
| ------- | ------- | ------- | --- |
| `YamlDotNet` | 16.2.1 | `Forge.Packaging`, `Forge.Environments` | `manifest.yaml`, bundle manifests, config file |
| `MySqlConnector` | 2.4.0 | `Forge.Database` | MySQL, multi-statement batches |
| `BouncyCastle.Cryptography` | 2.4.0 | `Forge.Catalog` | Ed25519 |
| `System.CommandLine` | 2.0.0-beta4.22272.1 | `Forge.Cli` | Verb groups and parsing |
| `Microsoft.NET.Test.Sdk` | 17.11.1 | test projects | — |
| `xunit` | 2.9.2 | test projects | — |
| `xunit.runner.visualstudio` | 2.8.2 | test projects | — |

Nothing else. In particular:

- **No `FluentAssertions`.** Version 8 moved to a commercial licence. Plain
  `Assert` is sufficient.
- **No `Newtonsoft.Json`.** `System.Text.Json` is in-box, and canonicalisation is
  implemented directly against its reader.
- **No `Testcontainers`.** See integration tests below.
- **No DI container, no `AutoMapper`, no mediator.** `Forge.Cli` constructs what
  it needs.

`YamlDotNet` throws on unmatched properties by default, which is what
[SQL.md](SQL.md) and [DEPLOY.md](DEPLOY.md) require. Do **not** call
`IgnoreUnmatchedProperties()` on the deserialiser builder: it opts out of exactly
the behaviour the `E_MANIFEST_INVALID` and `E_BUNDLE_INVALID` rules depend on.

---

# Database Execution

Details that decide whether migration content written for the `mysql` client also
runs through `MySqlConnector`.

| Concern | Requirement |
| ------- | ----------- |
| User variables | The migration connection string MUST set `AllowUserVariables=True`. application content commonly uses `SET @entry = ...` across statements, and the default connection rejects it. |
| Multi-statement batches | A whole migration file is one `MySqlCommand.CommandText`. `MySqlConnector` executes semicolon-separated statements in one command; no client-side splitting, per [SQL.md](SQL.md). |
| Connection lifetime | **One connection per migration file**, opened before and closed after. A migration may set session variables or `sql_mode`; a shared connection would leak that state into the next migration and make the run order-dependent in a way nothing records. |
| `sql_mode` | The tool MUST NOT set or change `sql_mode`. A package needing a non-default mode sets it itself, inside the migration, where it is visible in review. |
| Transactions | The tool opens the transaction. `START TRANSACTION`, `COMMIT`, and `ROLLBACK` are forbidden in migration content. DDL still commits implicitly — see Failure Semantics in [SQL.md](SQL.md). |
| Timeouts | No command timeout on a migration. A large content import legitimately runs for minutes, and a timeout mid-batch produces exactly the partial state the format works to avoid. |
| Tool-issued SQL | Parameterised, always. The only text sent verbatim is migration and dump content the operator supplied. |

---

# CLI Surface

One executable, `forge`, three verb groups. Nothing outside this table exists in
V1.

## Global Options

| Option | Default | Notes |
| ------ | ------- | ----- |
| `--config <path>` | `$FORGE_CONFIG`, else `~/.config/forge/config.yaml` | — |
| `--state <path>` | `$FORGE_STATE`, else `~/.local/share/forge` | Trust store and environment state. |
| `--verbose` | off | More detail on stderr. Never changes exit codes. |
| `--json` | off | Machine-readable result on stdout. Human text otherwise. |
| `--yes` | off | Pre-answers confirmations. Explicitly does **not** apply to `environment delete`. |

## forge package

| Command | Arguments | Notes |
| ------- | --------- | ----- |
| `install` | `<path-or-name>` `[--database <name>]` `[--dry-run]` | A path installs a local directory; a name resolves through catalogs. `--dry-run` prints the plan and exits 0 without writing. |
| `upgrade` | `<path-or-name>` `[--database <name>]` `[--dry-run]` | — |
| `remove` | `<name>` `[--database <name>]` | Requires `rollback/` coverage. |
| `list` | `[--database <name>]` | Reads the tracking tables. |
| `validate` | `<path>` | Manifest, migration names, content rules, package hash. No database contact, no connection attempted. |
| `hash` | `<path>` | Prints the package hash. |
| `search` | `<term>` | Phase 2. Searches trusted catalogs. |

`--database` names the target database, defaulting to the configured primary
database. The `spec.targets.database` value in the manifest selects which
*configured* database is used; `--database` overrides the name, not the manifest's
intent.

## forge catalog

| Command | Arguments | Notes |
| ------- | --------- | ----- |
| `keygen` | `[--out <path>]` | Generates an Ed25519 keypair. Private key written with owner-only permissions. |
| `publish` | `<directory>` `--key <path>` `[--out index.json]` | Builds and signs a catalog over the artifacts in a directory. |
| `verify` | `<path-or-url>` | Runs the full verification pipeline and reports which step failed. |
| `add` | `<url>` | Fetches, verifies, shows the publisher, and prompts for trust. |
| `trust` | `<key-or-url>` | Explicit enrolment. Prints the key being trusted. |
| `untrust` | `<key>` | — |
| `list` | `[--trusted]` | Known catalogs, or the trust store. |
| `discover` | `<url>` `[--depth <n>]` | Walks recommendations and reports what was found. Enrols nothing. |
| `fetch` | `<publisher> <artifact> <version>` `[--out <path>]` | Downloads and verifies the digest. Never executes. |

## forge environment

| Command | Arguments | Notes |
| ------- | --------- | ----- |
| `deploy` | `<bundle>` `[--dry-run]` | The sequence in [DEPLOY.md](DEPLOY.md). `--dry-run` prints the plan, contacts nothing. |
| `start` | `<name>` | Idempotent. |
| `stop` | `<name>` | Idempotent. |
| `backup` | `<name>` | — |
| `delete` | `<name>` `--confirm <name>` | `--confirm` must repeat the environment name. `--yes` and `--force` do not bypass it. |
| `list` | — | — |

---

# Exit Codes

`Forge.Cli` maps every `Result<T>` error to one of these. A command that succeeds
returns 0 and writes its result to stdout.

| Code | Class | Meaning |
| ---- | ----- | ------- |
| 0 | success | — |
| 1 | internal | An exception escaped a layer. A bug; the message says so. |
| 2 | usage | Bad arguments, unknown command. Produced by `System.CommandLine`. |
| 3 | configuration | Missing or invalid config, unreachable database, absent template. |
| 10 | validation | A manifest, bundle, migration, or catalog document is invalid. |
| 11 | verification | A signature or digest did not verify. |
| 12 | trust | The operation required trust that has not been granted. |
| 13 | state | The requested change conflicts with recorded state. |
| 14 | resolution | Dependencies or conflicts cannot be satisfied. |
| 15 | execution | A migration, dump, or container operation failed. |
| 16 | limits | A discovery, size, or count limit was reached. |

Mapping from the format documents:

| Error code | Exit |
| ---------- | ---- |
| `E_MANIFEST_INVALID`, `E_BUNDLE_INVALID`, `E_MIGRATION_NAME_INVALID`, `E_MIGRATION_DUPLICATE_PREFIX`, `E_MIGRATION_FORBIDDEN_STATEMENT`, `E_SPEC_VERSION_UNSUPPORTED`, `E_CANONICALIZATION_FAILED` | 10 |
| `E_SIGNATURE_INVALID`, `E_DIGEST_MISMATCH`, `E_PACKAGE_HASH_MISMATCH`, `E_MIGRATION_MODIFIED`, `E_CATALOG_EXPIRED`, `E_CATALOG_ROLLBACK` | 11 |
| `E_PUBLISHER_UNTRUSTED`, `E_KEY_CHANGED` | 12 |
| `E_ALREADY_INSTALLED`, `E_NOT_INSTALLED`, `E_DOWNGRADE_REFUSED`, `E_ROLLBACK_UNAVAILABLE`, `E_INSTALL_INTERRUPTED`, `E_ENVIRONMENT_EXISTS`, `E_ENVIRONMENT_ID_TAKEN`, `E_ENVIRONMENT_NOT_DEPLOYED`, `E_DEPLOY_INTERRUPTED`, `E_PORT_UNAVAILABLE` | 13 |
| `E_DEPENDENCY_UNSATISFIED`, `E_DEPENDENCY_CYCLE`, `E_CONFLICT_DECLARED`, `E_TARGET_MISMATCH` | 14 |
| `E_MIGRATION_FAILED`, `E_CONTAINER_FAILED` | 15 |
| `E_CONFIG_KEY_MISSING`, `E_CONFIG_FORMAT_UNEXPECTED`, `E_REGISTRY_SCHEMA_UNEXPECTED`, `E_ANALYTICS_SCHEMA_MISSING` | 3 |
| `E_LIMIT_EXCEEDED`, `E_CYCLE_DETECTED` | 16 |
| `E_SCHEME_UNSUPPORTED` | 10 |

Every failure prints the error code, then a message naming the file, package,
field, or column at fault. A message that does not identify what to fix is
incomplete.

---

# Configuration

One file, no discovery beyond the documented path. Nothing is inferred from the
working directory.

```yaml
apiVersion: forge.v1
kind: Config

spec:
  core: example-app

  database:
    host: 127.0.0.1
    port: 3306
    user: forge
    # password comes from FORGE_DB_PASSWORD when absent here
    primary: app_primary
    analytics: analytics
    identity: identity

  environment:
    configTemplate: /opt/example-app/etc/application.conf.dist
    analyticsSchema: /opt/example-app/sql/base/analytics.sql
    image: example/application-server:latest
    assets: /srv/example-app/data
    basePort: 8085
    # the host an application client connects to; written to forge_environment.address
    advertisedAddress: environments.example.com

  catalog:
    limits:
      depth: 5
      catalogs: 250
      catalogBytes: 10485760
      artifacts: 10000
      redirects: 5
      timeoutSeconds: 30
```

| Environment variable | Effect |
| -------------------- | ------ |
| `FORGE_CONFIG` | Config file path. |
| `FORGE_STATE` | State root for the trust store and environment state. |
| `FORGE_DB_PASSWORD` | Database password; overrides the file. |
| `FORGE_TEST_MYSQL` | Integration-test connection string. Required by `make test-integration`; unused by `make test`, which excludes those tests. |

Rules:

- A config file containing a password must be owner-readable only; the tool warns
  otherwise and continues.
- Credentials are never written to stdout, stderr, `state.json`, or a backup
  manifest. A generated `application.conf` does contain them and is written
  owner-only.
- `spec.catalog.limits` defaults match [SPEC.md](SPEC.md). An absent key takes the
  default; a present key is used as given, including a lower one.
- `spec.environment.advertisedAddress` has **no default**. `forge environment deploy` fails
  with exit 3 when it is absent, because every value the tool could guess —
  `localhost`, the database host, a container IP — produces an environment that clients
  see and cannot join.

---

# State On Disk

```
<state-root>/
├── trust.json                    publishers keyed by base64 public key
├── catalogs/
│   └── <sha256-of-url>.json      verified catalog cache
└── environments/
    └── <name>/
        ├── state.json
        ├── application.conf
        ├── compose.yaml
        └── backups/<timestamp>/
```

`trust.json` holds, per publisher: public key, algorithm, display name last seen,
highest catalog version accepted, and when trust was granted. The public key is
the key of the map — not the publisher `id`, which is not unique.

A cached catalog is stored only after it passed every check. The cache is keyed by
URL hash so two publishers using the same `id` cannot collide.

---

# Testing

## Layers

| Kind | Project | Needs | Run by |
| ---- | ------- | ----- | ------ |
| Unit | `Forge.Packaging.Tests`, `Forge.Catalog.Tests`, `Forge.Environments.Tests` | nothing | `make test` |
| Architecture | `Forge.Architecture.Tests` | nothing | `make test` |
| Integration | `Forge.Database.Tests` | MySQL via `FORGE_TEST_MYSQL` | `make test-integration` |
| End-to-end | `Forge.EndToEnd.Tests` | MySQL, and Docker for Phase 3 | `make test-integration` |

`make test` must pass on a machine with no MySQL and no Docker.

Integration tests are marked `[Trait("Category", "Integration")]` and `make test`
excludes them by filter, so they are never run in that mode rather than being
skipped at runtime. xUnit 2.x has no `Assert.Skip` — dynamic skipping needs
either xUnit v3 or the `SkippableFact` package, and neither is worth adding when
a trait filter expresses the same thing at the command line.

Run on their own with `FORGE_TEST_MYSQL` unset, integration tests **fail** with a
message naming the variable. They do not quietly pass: a suite that reports green
without having reached a database is worse than one that reports red.

`Testcontainers` is not used: this repository is worked on inside a devcontainer
that already drives Docker, and nesting a container runtime inside it to start
MySQL adds a failure mode with no test value. `ops/docker/docker-compose.yml`
starts MySQL; `make db-up` and `make db-down` wrap it.

Each integration test owns a database named `forge_test_<guid>`, created in setup
and dropped in teardown, so tests do not share state and a crashed run leaves an
obvious orphan.

## Architecture Tests

`Forge.Architecture.Tests` asserts the dependency rules in [HLD.md](HLD.md) by
reading each project's assembly references. It is not optional decoration: the
rule that `Forge.Catalog` cannot see `Forge.Packaging` is the design's load-bearing
claim, and it is one careless `using` away from being false.

## Fixtures

`fixtures/` is committed input, never generated.

| Path | Holds |
| ---- | ----- |
| `fixtures/packages/valid-minimal/` | Manifest plus one migration. |
| `fixtures/packages/valid-full/` | Every optional field, `rollback/`, `assets/`. |
| `fixtures/packages/invalid-*/` | One per validation failure, named for the error code. |
| `fixtures/packages/chain-{a,b,c}/` | A dependency chain. |
| `fixtures/packages/cycle-{a,b}/` | A dependency cycle. |
| `fixtures/packages/conflict-{a,b}/` | A declared conflict. |
| `fixtures/catalogs/valid/` | Catalog, keypair, artifact. |
| `fixtures/catalogs/invalid-*/` | One per failure in [SPEC.md](SPEC.md). |
| `fixtures/catalogs/unknown-fields/` | A signed catalog carrying extension fields, for the round-trip test. |
| `fixtures/environments/*/` | Bundles, plus a config template missing a key. |

Fixture keypairs are test keys committed on purpose. They sign nothing real, and
the private keys are named `test-only.key` so no reader mistakes them for
secrets.

---

# Make Targets

`make validate` is the gate. It is what CI runs, what an automated implementation
session runs to check its own work, and the single command that must pass before
a work item is complete.

```make
setup:              dotnet restore
build:              dotnet build --no-restore
test:               dotnet test --no-build --filter Category!=Integration
test-integration:   dotnet test --no-build --filter Category=Integration
validate:           setup build test
db-up:              docker compose -f ops/docker/docker-compose.yml up -d
db-down:            docker compose -f ops/docker/docker-compose.yml down -v
format:             dotnet format
clean:              dotnet clean
```

`validate` deliberately excludes integration tests: it must pass with no
services, so it stays usable as an unattended gate. Integration tests run behind
`make db-up && make test-integration`.

---

# Implementation Protocol

For an automated implementation session working through [HLD.md](HLD.md):

- One work item per session, in the order listed. A work item ends with
  `make validate` passing.
- Work only inside this directory. It is a self-contained experiment and shares
  no code with any other directory in the monorepo, by the rule in the root
  `AGENTS.md`.
- Read the normative document for the phase before starting a work item in it. The
  work-item line is a brief; the format document decides.
- When a work item needs a fact that is not in these documents, stop and report
  the gap. Do not infer it. The *Do Not Invent* list in [HLD.md](HLD.md) says
  where this matters most.
- Do not add abstraction for work items not yet reached. An interface with one
  implementation and one caller is not preparation, it is a guess about the next
  work item.
- Record what was missing, wrong, or ambiguous in these documents as the session
  goes. That record is the experiment's actual output; the code is the artifact
  that proves the documents were sufficient.

The last point is the point. This is an experiment in whether a specification can
be written well enough to be implemented from, so a session that had to guess
something has found the result, not a nuisance.
