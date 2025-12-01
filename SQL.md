# SQL Package Format

**Version:** 1.0
**Status:** Normative for Phase 1 of this repository
**Layer:** 2 of 3 — see [README.md](README.md)

A packaging layer that wraps conventional SQL migrations with metadata,
versioning, dependency declaration, and installation tracking.

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY are to be interpreted
as described in RFC 2119.

## Scope

This document defines the package format and the semantics of installing,
upgrading, and removing a package against a MySQL-compatible database.

It does not define how packages are distributed. A package installs from a local
directory with no catalog involved; [SPEC.md](SPEC.md) is how a package
optionally travels.

It does not define a SQL dialect. The content of a migration is standard SQL,
executable by the `mysql` client directly.

---

# Package Structure

A package is a directory. Its name on disk is not significant; the manifest
names it.

```
example-package/
├── manifest.yaml        required
├── README.md            optional
├── LICENSE              optional
├── migrations/          required, at least one file
│   ├── 001_initial.sql
│   ├── 002_resources.sql
│   ├── 003_indexes.sql
│   └── 004_balance.sql
├── rollback/            optional, see Removal
│   ├── 004_balance.sql
│   ├── 003_indexes.sql
│   ├── 002_resources.sql
│   └── 001_initial.sql
└── assets/              optional, never executed
    └── icon.png
```

Rules:

- `manifest.yaml` and a non-empty `migrations/` directory are required. A
  directory missing either is not a package (`E_MANIFEST_INVALID`).
- Files outside `migrations/` and `rollback/` are never executed. `assets/` is
  data for a presentation layer.
- A package MUST NOT contain symbolic links, and a reader MUST refuse one rather
  than following it.
- Paths MUST be relative and MUST NOT contain `..` segments.

---

# Manifest

`manifest.yaml` describes the package and contains no implementation logic.

```yaml
apiVersion: sqlpackage.v1
kind: Package

metadata:
  name: customer-profiles
  version: 1.2.0
  description: Customer profile resources
  author: Jon
  homepage: https://example.com/customer-profiles
  license: MIT

spec:
  targets:
    core: example-app
    database: primary

  dependencies:
    - name: base-schema
      version: ">=1.0.0 <2.0.0"
    - name: extension-framework
      version: "=2.1.0"

  conflicts:
    - name: customer-indexes
      version: "*"
```

| Field | Type | Required | Notes |
| ----- | ---- | -------- | ----- |
| `apiVersion` | string | yes | `sqlpackage.v1`. A reader MUST reject other values. |
| `kind` | string | yes | `Package`. |
| `metadata.name` | string | yes | `[a-z0-9][a-z0-9-]{0,63}`. Identifies the package within a database. |
| `metadata.version` | string | yes | Semantic Versioning 2.0.0. |
| `metadata.description` | string | yes | One line, 1–256 characters. |
| `metadata.author` | string | no | Free text. Presentation only — it is not identity. Identity is the publisher key in [SPEC.md](SPEC.md). |
| `metadata.homepage` | string | no | `https` URL. |
| `metadata.license` | string | no | SPDX identifier where one applies. |
| `spec.targets.core` | string | yes | An opaque identifier for the core the package targets. Matched by exact string equality against the operator's configured core. See [Target Matching](#target-matching). |
| `spec.targets.database` | string | yes | `primary`, `analytics`, or `identity`. Selects which database the migrations apply to. |
| `spec.dependencies` | array | no | See [Dependencies](#dependencies). |
| `spec.conflicts` | array | no | See [Conflicts](#conflicts). |

Unknown fields MUST be rejected rather than ignored (`E_MANIFEST_INVALID`). This
is the opposite of the catalog rule in [SPEC.md](SPEC.md), and deliberately so: a
catalog is signed and travels between versions of a client, while a manifest is
read once by the tool that is about to write to a database. A typo'd manifest
key is a mistake worth surfacing, not a forward-compatibility case.

## Target Matching

`spec.targets.core` is an opaque string. The tool MUST compare it for exact
equality against the core identifier the operator configured, and MUST refuse a
mismatch (`E_TARGET_MISMATCH`).

The tool MUST NOT attempt to detect the running core, infer compatibility from a
version number, or maintain a table of known core identifiers. It has no
reliable way to do any of those, and guessing wrong means writing content into a
primary database that cannot use it.

---

# Versions And Constraints

`metadata.version` is Semantic Versioning 2.0.0.

A dependency or conflict constraint uses this grammar:

```
constraint := "*" | clause (" " clause)*
clause     := op version
op         := "=" | ">=" | ">" | "<=" | "<"
version    := a SemVer 2.0.0 version, without build metadata
```

Rules:

- All clauses must hold. Clauses are ANDed; there is no OR.
- `*` matches any version.
- `^` and `~` are not supported. A reader MUST reject them rather than
  interpreting them, because their meaning varies between ecosystems.
- Pre-release versions are excluded unless a clause names a pre-release
  explicitly, following SemVer §11. `>=1.0.0` does not match `1.1.0-beta.1`.
- Build metadata is ignored in comparison and MUST NOT appear in a constraint.

---

# Migrations

A package's work is ordered SQL migrations. Each migration is one logical
change: add tables, add indexes, add reference data, or update views.

## Naming And Ordering

```
migrations/<NNN>_<slug>.sql
```

| Part | Rule |
| ---- | ---- |
| `<NNN>` | Three or more decimal digits, zero-padded. Unique within the package. |
| `_` | A single underscore separator. |
| `<slug>` | `[a-z0-9][a-z0-9-]*`, describing the change. |
| `.sql` | Lowercase extension. |

Rules:

- Migrations are ordered by the numeric value of `<NNN>`, not by string sort, so
  `010` follows `009`.
- Two migrations with the same numeric prefix are an error
  (`E_MIGRATION_DUPLICATE_PREFIX`), even when the slugs differ. There is no
  defined order between them.
- A filename not matching the pattern is an error
  (`E_MIGRATION_NAME_INVALID`). It is not skipped — a skipped migration is a
  silently incomplete install.
- Gaps in numbering are allowed.

Migrations SHOULD be small, independently reviewable, and idempotent where
practical. Idempotence matters because recovery is forward-only; see
[Failure Semantics](#failure-semantics).

## Content Rules

A migration MUST NOT contain:

| Forbidden | Why |
| --------- | --- |
| `DELIMITER` | A `mysql` client directive, not server SQL. The server rejects it, and supporting it would mean reimplementing the client's parser. |
| `USE <db>` | The runner selects the database from `spec.targets.database`. A migration that switches databases escapes that choice. |
| `CREATE DATABASE`, `DROP DATABASE` | Database lifecycle belongs to the environment layer, not to content. |
| `START TRANSACTION`, `COMMIT`, `ROLLBACK` | The runner owns transaction boundaries. |
| `SOURCE` | A client directive that would read arbitrary host files. |

A reader MUST reject a migration containing any of these
(`E_MIGRATION_FORBIDDEN_STATEMENT`) and MUST name the file and line.

Routines that need a custom statement delimiter cannot be expressed and are out
of scope for v1. A package needing one should ship the routine body through a
single `CREATE PROCEDURE` statement sent as one batch, which requires no
delimiter change.

## Execution

Each migration file is sent to the server as a **single multi-statement batch**.

The runner MUST NOT split the file on semicolons. Client-side splitting requires
a full SQL lexer to handle semicolons inside string literals, comments, and
routine bodies, and a partial implementation corrupts exactly the content this
format exists to distribute.

Each migration is executed inside an explicit transaction opened by the runner.
See [Failure Semantics](#failure-semantics) for what that does and does not
guarantee.

Each migration gets its own database session. A migration MAY set session
variables and `sql_mode` — content SQL commonly does — and a session shared
between migrations would carry that state into the next file, making the result
depend on an ordering that nothing records. The runner MUST NOT set `sql_mode`
itself.

User variables (`SET @entry = ...`) are common in application content and MUST work.
The client library's connection settings are the implementation's concern, and
[TECHNICAL.md](TECHNICAL.md) states them.

---

# Installation Tracking

The tool maintains two tables in the target database. Both are prefixed to avoid
colliding with core schema, and neither is referenced by the core.

```sql
CREATE TABLE IF NOT EXISTS forge_installed_package (
    name             VARCHAR(64)  NOT NULL,
    version          VARCHAR(64)  NOT NULL,
    package_hash     CHAR(64)     NOT NULL,
    target_core      VARCHAR(128) NOT NULL,
    installed_at     DATETIME     NOT NULL,
    installed_by     VARCHAR(128) NOT NULL,
    state            VARCHAR(16)  NOT NULL,
    PRIMARY KEY (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS forge_applied_migration (
    package_name     VARCHAR(64)  NOT NULL,
    migration_file   VARCHAR(255) NOT NULL,
    package_version  VARCHAR(64)  NOT NULL,
    migration_digest CHAR(64)     NOT NULL,
    applied_at       DATETIME     NOT NULL,
    duration_ms      INT UNSIGNED NOT NULL,
    PRIMARY KEY (package_name, migration_file),
    CONSTRAINT fk_applied_package
        FOREIGN KEY (package_name) REFERENCES forge_installed_package (name)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

The primary key is `(package_name, migration_file)` and deliberately excludes
`package_version`. A migration file is applied at most once in the lifetime of a
package's installation, whatever version applied it, so "has this migration run"
is a primary-key lookup rather than a question about versions. Including the
version in the key would let `001_initial.sql` be recorded once per version and
re-applied on every upgrade.

| Column | Meaning |
| ------ | ------- |
| `forge_installed_package.state` | `installing`, `installed`, or `failed`. Written `installing` before the first migration and updated when the run ends. |
| `forge_installed_package.package_hash` | See [Package Hash](#package-hash). Lets a later run detect that the package contents changed. |
| `forge_installed_package.installed_by` | An operator-supplied label, defaulting to the OS user. Audit only. |
| `forge_applied_migration.package_version` | The package version that applied this migration. Recorded for audit; not part of the key. |
| `forge_applied_migration.migration_digest` | Lowercase hex SHA-256 of the migration file bytes. Detects a migration edited after it was applied. |

Rules:

- The tool MUST create these tables if absent, before the first migration.
- The tool MUST record each migration immediately after it succeeds, in the same
  transaction as the migration where the database allows it.
- A row in `state = 'installing'` on a later run means a previous run died
  mid-install. The tool MUST report it and MUST NOT silently resume.

## Package Hash

The package hash identifies the contents of a package directory, independently of
how it was archived or transported.

Computation:

1. Walk the package directory recursively and collect every file. Include
   `manifest.yaml`, `migrations/`, `rollback/`, `assets/`, and every other file.
2. For each file, take its path relative to the package root, with `/`
   separators, and compute the SHA-256 of its bytes as lowercase hex.
3. Sort the entries by the UTF-8 bytes of the relative path, ordinal ascending.
4. For each entry emit `<relative-path>` `NUL` `<digest-hex>` `LF`.
5. The package hash is the SHA-256 of that byte sequence, as lowercase hex.

A NUL separator is used because a filename may contain a space but not a NUL, so
the encoding is unambiguous without quoting.

This is **not** the same value as an artifact `digest` in [SPEC.md](SPEC.md). The
artifact digest covers the archive bytes as distributed; the package hash covers
the contents as extracted. A package can be re-archived — different compression,
different timestamps — and keep its package hash while the artifact digest
changes. Both are needed, and a client MUST NOT substitute one for the other.

---

# Operations

## Install

1. Read and validate the manifest.
2. Validate every migration filename and content rule.
3. Check `spec.targets.core` against the configured core
   (`E_TARGET_MISMATCH`).
4. Read `forge_installed_package`. If the package is present and `installed`,
   stop (`E_ALREADY_INSTALLED`).
5. Resolve dependencies (see [Dependencies](#dependencies)). Any unsatisfiable
   constraint stops the whole operation before a single statement runs
   (`E_DEPENDENCY_UNSATISFIED`).
6. Check conflicts against installed packages (`E_CONFLICT_DECLARED`).
7. Create the tracking tables if absent.
8. Insert the package row with `state = 'installing'`.
9. Apply migrations in order, recording each one.
10. Update the row to `state = 'installed'`.

Dependencies are installed before the package that needs them, in topological
order. The whole plan is computed and reported before execution begins.

## Upgrade

1. Read the installed row. Absent means nothing to upgrade
   (`E_NOT_INSTALLED`).
2. Compare the installed version to the candidate version.
   - Equal: nothing to do.
   - Candidate lower: refuse (`E_DOWNGRADE_REFUSED`). Downgrading means undoing
     applied SQL, which requires the rollback path and is a `remove` followed by
     an `install`, stated explicitly by the operator.
3. Verify that every migration already recorded for this package still has a
   matching digest in the candidate package
   (`E_MIGRATION_MODIFIED`). A migration that was rewritten between versions
   means the database's history no longer matches the package's, and the tool
   cannot know what the difference did.
4. Apply only migrations absent from `forge_applied_migration` for this package,
   in numeric order.
5. Update the row's `version` and `package_hash`.

Upgrade never reimports and never drops content. Migrations already applied are
not re-run.

## Remove

1. Read the installed row (`E_NOT_INSTALLED` if absent).
2. Require a `rollback/` file for every applied migration, matched by the same
   `<NNN>_<slug>.sql` name. Incomplete coverage refuses the operation
   (`E_ROLLBACK_UNAVAILABLE`) and names the missing files.
3. Refuse if another installed package declares a dependency on this one
   (`E_DEPENDENCY_UNSATISFIED`), naming the dependants.
4. Execute rollback files in **reverse** migration order.
5. Delete the tracking rows.

A package with no `rollback/` directory cannot be removed. This is stated in the
format rather than worked around: undoing arbitrary SQL cannot be derived, and a
tool that guessed would delete a primary's content.

## List

Read `forge_installed_package` and report name, version, state, install time,
and whether the on-disk package still matches `package_hash` when a package
directory is available.

---

# Dependencies

```yaml
dependencies:
  - name: base-schema
    version: ">=1.0.0 <2.0.0"
```

Resolution:

1. Build a graph from the requested package's declared dependencies,
   transitively, over the packages available in the configured sources.
2. A cycle is an error (`E_DEPENDENCY_CYCLE`), naming the cycle.
3. For each package, select the highest available version satisfying every
   constraint placed on it by any dependant.
4. If no version satisfies all constraints, the operation fails
   (`E_DEPENDENCY_UNSATISFIED`), naming the package and the conflicting
   constraints.
5. Order the result topologically; dependants install after dependencies.

A dependency is satisfied by an already-installed package at a satisfying
version. It is not reinstalled.

Dependencies are resolved by `name` within one target database. Two packages of
the same name from different publishers cannot both be installed; the first one
installed owns the name, and the second is a conflict the operator must resolve.
This is narrower than the catalog layer's identity model in
[SPEC.md](SPEC.md) — deliberately, because the database is the shared namespace
and it has one row per name.

---

# Conflicts

```yaml
conflicts:
  - name: customer-indexes
    version: "*"
```

A conflict declares that two packages MUST NOT be installed into the same
database. The tool MUST check conflicts in both directions: the candidate's
declarations against what is installed, and the installed packages'
declarations against the candidate.

A declared conflict is refused (`E_CONFLICT_DECLARED`) and MUST NOT be
overridable by a flag. The publisher who declared it knows something the tool
does not.

---

# Failure Semantics

DDL in MySQL is not transactional. `CREATE TABLE`, `ALTER TABLE`, and their
relatives cause an implicit commit, so a transaction wrapping a migration cannot
roll back the DDL it contains.

What this means, stated plainly because it determines the recovery model:

- A migration containing only DML (`INSERT`, `UPDATE`, `DELETE`) rolls back
  cleanly on failure.
- A migration containing DDL may leave the database part-applied, with no way to
  undo the committed portion.
- Therefore recovery is **forward-only**. There is no "undo the failed install"
  operation.

On a migration failure the tool MUST:

1. Roll back the current transaction, recovering whatever is recoverable.
2. Leave `forge_installed_package.state = 'failed'` with the version it was
   attempting.
3. Leave the successfully applied migrations recorded in
   `forge_applied_migration`.
4. Report the failing file, the server error, and the migrations that did apply.
5. Stop. It MUST NOT continue to the next migration, and MUST NOT attempt
   compensating SQL it invented.

Recovery is the operator's: fix the migration or the database, then re-run. A
re-run applies only the unrecorded migrations, which is why migrations SHOULD be
idempotent — an idempotent migration can be safely re-run after a partial
failure inside it.

A package that must not be left half-applied should keep DDL and DML in separate
migrations, so a DML failure is recoverable and a DDL migration is small enough
to reason about.

---

# Error Conditions

| Code | Meaning |
| ---- | ------- |
| `E_MANIFEST_INVALID` | Missing, unparseable, wrong `apiVersion`, failed field validation, or an unknown field. |
| `E_MIGRATION_NAME_INVALID` | A file in `migrations/` does not match `<NNN>_<slug>.sql`. |
| `E_MIGRATION_DUPLICATE_PREFIX` | Two migrations share a numeric prefix. |
| `E_MIGRATION_FORBIDDEN_STATEMENT` | A migration contains a forbidden statement. |
| `E_MIGRATION_MODIFIED` | A recorded migration's digest does not match the package's copy. |
| `E_MIGRATION_FAILED` | The server rejected a migration. |
| `E_TARGET_MISMATCH` | `spec.targets.core` does not equal the configured core. |
| `E_DEPENDENCY_UNSATISFIED` | No available version satisfies the constraints, or a dependant blocks a removal. |
| `E_DEPENDENCY_CYCLE` | The dependency graph contains a cycle. |
| `E_CONFLICT_DECLARED` | A declared conflict is present in the target database. |
| `E_ALREADY_INSTALLED` | The package is already installed at this version. |
| `E_NOT_INSTALLED` | The operation requires an installed package and found none. |
| `E_DOWNGRADE_REFUSED` | The candidate version is lower than the installed version. |
| `E_ROLLBACK_UNAVAILABLE` | Removal requested without complete `rollback/` coverage. |
| `E_PACKAGE_HASH_MISMATCH` | The package directory's contents do not match the recorded hash. |
| `E_INSTALL_INTERRUPTED` | A tracking row was found in `state = 'installing'`. |

---

# Distribution Binding

How a package becomes a catalog artifact. This is the only place the two layers
meet, and the direction is one-way: this document knows about
[SPEC.md](SPEC.md), and [SPEC.md](SPEC.md) does not know about this one.

| Binding | Value |
| ------- | ----- |
| Archive format | ZIP. |
| Archive layout | Entries are relative to the package root, with no wrapping directory. `manifest.yaml` is at the archive root. |
| `mediaType` | `application/vnd.oac.sqlpackage.v1+zip` |
| Artifact `id` | MUST equal `metadata.name`. |
| Artifact `version` | MUST equal `metadata.version`. |
| Artifact `digest` | The SHA-256 of the archive bytes, per [SPEC.md](SPEC.md). Not the package hash. |

Rules for installing a package that arrived as an artifact:

1. Verify the artifact digest before extracting anything. An archive that failed
   verification is discarded unextracted.
2. Reject any entry whose path is absolute, contains a `..` segment, or is a
   symbolic link, before writing it. Extraction happens into a fresh directory
   the tool created.
3. After extraction, check that `metadata.name` equals the artifact `id` and
   `metadata.version` equals the artifact `version`. A mismatch is
   `E_MANIFEST_INVALID`, naming both values. The catalog's claim about what it
   is carrying and the package's claim about itself must agree, and the signature
   covers only the former.
4. Continue with the ordinary install path. An extracted package is a package
   directory; nothing downstream behaves differently because it was downloaded.
5. Resolve a package name to an artifact only among trusted publishers, and only
   among artifacts whose `mediaType` is the value above. An artifact of another
   media type that happens to share an `id` is not a candidate.

Where two trusted publishers both offer a package of the same name, the tool MUST
ask the operator which publisher to install from rather than choosing. The
database has one row per package name, as
[Dependencies](#dependencies) explains, so the choice is consequential and
unrecoverable without a removal.

---

# Future Repository Support

Packages become distributable through catalogs, which is Layer 1. Conceptually
the result resembles `apt`, Homebrew, NuGet, or Helm, with the registry replaced
by signed static catalogs.

```
forge package search customer-profiles
forge package install customer-profiles
forge package upgrade customer-profiles
forge package remove winter-event
```

The package format above is unchanged by this. A catalog carries a package as an
archived artifact; the artifact digest covers the archive and the package hash
covers the contents, and installation behaves identically whether the directory
came from a download or was always local.

---

# Design Philosophy

The format extends existing application workflows rather than replacing them.

Existing SQL stays valid. A creator's `INSERT` statements move into a numbered
file and gain a manifest; nothing about them is rewritten, wrapped, or
templated. The package system provides structure, metadata, ordering, and a
record of what happened — and deliberately provides nothing else, because
everything else would mean interpreting the SQL, and interpreting it is how a
packaging layer starts breaking the content it carries.
