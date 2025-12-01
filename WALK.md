# Walk: Five-Step End-to-End Success Walk

VISION.md "What Success Looks Like", executed from a clean machine. Evidence per step
is the command run, its output, and direct database queries — not tool output alone.

Walked 2026-09-29 against `2bbaef7cbcb0de081d2847289ec950d0ec0f4754`
(`[act-or-s] Deploy a second realm sharing the authentication database (#644)`).

## Clean state

| Thing | How it was made clean |
| ----- | --------------------- |
| Database server | `docker compose -f ops/docker/docker-compose.yml down -v` (fresh volume), then `up -d --wait` → MySQL 8.0, healthy. Databases present: `primary` (empty, from the compose environment), `information_schema`, `performance_schema`. |
| Checkout | `git archive HEAD sql/sql-package-bundle-for-resources` extracted to `/tmp/forge-walk-1/tree`, built with `make setup` and `make build` (0 warnings, 0 errors). `forge` = `src/Forge.Cli/bin/Debug/net8.0/forge` in that tree. |
| State directory | Empty directory `/tmp/forge-walk-1/state` (no trust store, no realm state). |
| Operator database | Per DEPLOY.md the operator owns `identity`. Created it and applied the operator-supplied schema `fixtures/auth/realmlist.sql`, and granted the operator user (`forge`) access to it. |

Config file `/tmp/forge-walk-1/config.yaml`, password supplied via `FORGE_DB_PASSWORD=forge-test`:

```yaml
apiVersion: forge.v1
kind: Config

spec:
  core: example-app
  database:
    host: 127.0.0.1
    port: 3306
    user: forge
    primary: primary
    identity: identity
  realm:
    configTemplate: /tmp/forge-walk-1/tree/sql/sql-package-bundle-for-resources/fixtures/realms/mangosd.conf.dist
    image: busybox:latest
    basePort: 8085
    advertisedAddress: 127.0.0.1
```

The host has no `mysql` client, so evidence queries run as
`docker exec forge-test-mysql mysql -uforge -pforge-test <database> -e "<sql>"`.

## Verdicts

| Step | Verdict | Where it failed, if it failed |
| ---- | ------- | ----------------------------- |
| 1. Trust | FAIL | The `forge catalog` verb group does not exist in the binary. |
| 2. Discover | FAIL | Same — no catalog surface in the binary. |
| 3. Install through the catalog | FAIL | `forge package install` takes only a local directory path; catalog name resolution is not implemented. The walk continued with the local-directory flow so steps 4–5 could run. |
| 4. Upgrade | PASS | — |
| 5. Second realm | PASS | — |

---

## Step 1 — Trust

Attempted the trust act through the tool as TECHNICAL.md's CLI surface documents it.

Command:

```
forge catalog add https://publisher.example/index.json
```

Output (stderr), exit 1:

```
Required command was not provided.
Unrecognized command or argument 'catalog'.
Unrecognized command or argument 'add'.
Unrecognized command or argument 'https://publisher.example/index.json'.
```

(the same usage dump as `forge --help` follows; it lists only `package` and `realm` commands)

Command:

```
forge catalog trust kK7k2mVl0t3oQ8n0Xk1Gm5rZ4xW9cB2vH6sT1yU3pA0=
```

Output (stderr), exit 1:

```
Required command was not provided.
Unrecognized command or argument 'catalog'.
Unrecognized command or argument 'trust'.
Unrecognized command or argument 'kK7k2mVl0t3oQ8n0Xk1Gm5rZ4xW9cB2vH6sT1yU3pA0='.
```

`forge --help` shows the complete command surface the walk has to work with:

```
Commands:
  package  SQL package operations (SQL.md).
  realm    Realm deployment operations (DEPLOY.md).
```

**Point of failure:** the trust step has no operator surface. TECHNICAL.md, CLI Surface,
documents a `forge catalog` group (`keygen`, `publish`, `verify`, `add`, `trust`,
`untrust`, `list`, `discover`, `fetch`); none of it is implemented in the `forge`
binary. The operator can never be shown a publisher's display name and key, and can
never perform the explicit trust act, with the shipped tool. The trust behaviour exists
only as library code with unit tests (`Forge.Catalog.Tests/TrustEnrolmentTests.cs`);
there is no executable path to it.

**Verdict: FAIL.**

## Step 2 — Discover

Command:

```
forge catalog discover https://publisher.example/index.json
```

Output (stderr), exit 1:

```
Required command was not provided.
Unrecognized command or argument 'catalog'.
Unrecognized command or argument 'discover'.
Unrecognized command or argument 'https://publisher.example/index.json'.
```

Command:

```
forge package search customer-profiles
```

Output (stderr), exit 1:

```
Required command was not provided.
Unrecognized command or argument 'search'.
Unrecognized command or argument 'customer-profiles'.
```

**Point of failure:** same missing catalog surface as Step 1. `discover` and `search`
are both documented (TECHNICAL.md, CLI Surface) and both absent. No catalog was
readable through the tool, so no fixture package could appear with version and
digest.

**Verdict: FAIL.**

## Step 3 — Install through the catalog

The documented install-by-name form (`install <path-or-name>`, TECHNICAL.md) is the
catalog path. Attempted it first:

Command:

```
forge package install customer-profiles --config /tmp/forge-walk-1/config.yaml
```

Output (stderr), exit 10:

```
E_MANIFEST_INVALID: package directory '/tmp/forge-walk-1/tree/sql/sql-package-bundle-for-resources/customer-profiles' does not exist
```

The name is treated as a local directory path and rejected; `forge package --help`
describes the argument as `install <path>  Install a package from a local directory.`
There is no resolution through trusted catalogs in the binary.

Evidence query — the failed attempt wrote nothing (run before any install):

```
SHOW TABLES;   -- in primary
```

Result: no tables.

**Point of failure:** catalog-mediated install has no CLI surface. The behaviour
exists as integration tests (`Forge.Database.Tests/CatalogInstallTests.cs`) but is not
wired to the binary.

The walk continued with the local-directory flow, which VISION.md defines as a
supported layer-2-without-layer-1 path ("A package installs from a local directory
with no catalog involved"), so Steps 4–5 could still run:

Command:

```
forge package install fixtures/packages/customer-profiles-v1 --config /tmp/forge-walk-1/config.yaml
```

Output (stdout), exit 0:

```
installing customer-profiles 1.0.0 (2 migrations)
installed customer-profiles 1.0.0
```

Evidence queries (in `primary`), run directly against the database:

```
SELECT name, version, target_core, state, installed_by FROM forge_installed_package;
```

| name | version | target_core | state | installed_by |
| ---- | ------- | ----------- | ----- | ------------ |
| customer-profiles | 1.0.0 | example-app | installed | node |

```
SELECT package_name, migration_file, package_version FROM forge_applied_migration ORDER BY migration_file;
```

| package_name | migration_file | package_version |
| ------------ | -------------- | --------------- |
| customer-profiles | 001_customer_profiles.sql | 1.0.0 |
| customer-profiles | 002_profile_contacts.sql | 1.0.0 |

```
SELECT * FROM cp_customer_profile ORDER BY entry;
```

| entry | name | status |
| ----- | ---- | ------ |
| 400001 | Northwind Supplies | 1 |
| 400002 | Southridge Services | 2 |

`forge package list --config /tmp/forge-walk-1/config.yaml` → `customer-profiles 1.0.0 installed`, exit 0.

The database answers what is installed; it just cannot be installed *through the
catalog* with the shipped binary.

**Verdict: FAIL** for the step as specified ("through the catalog"). The
local-directory install recorded here is the continuation that keeps Steps 4–5
reachable, not a substitute for the catalog path.

## Step 4 — Upgrade

Command:

```
forge package upgrade fixtures/packages/customer-profiles-v2 --config /tmp/forge-walk-1/config.yaml
```

Output (stdout), exit 0:

```
upgrading customer-profiles 1.1.0 (3 migrations)
upgraded customer-profiles to 1.1.0
```

Evidence queries — pre-upgrade, the two applied migrations and their digests:

```
SELECT migration_file, migration_digest, package_version FROM forge_applied_migration ORDER BY migration_file;
```

| migration_file | migration_digest | package_version |
| -------------- | ---------------- | --------------- |
| 001_customer_profiles.sql | 1a7d2ceb49b95321c8d41c05d4f846c1dc488a6483716d33a7320bc6871c5609 | 1.0.0 |
| 002_profile_contacts.sql | d8349d00bd1fa9565468c50a7115cdeebf5f2ccdccb48c5c7d4d0d658f3ac874 | 1.0.0 |

Post-upgrade:

```
SELECT name, version, state FROM forge_installed_package;
```

| name | version | state |
| ---- | ------- | ----- |
| customer-profiles | 1.1.0 | installed |

```
SELECT migration_file, migration_digest, package_version FROM forge_applied_migration ORDER BY migration_file;
```

| migration_file | migration_digest | package_version |
| -------------- | ---------------- | --------------- |
| 001_customer_profiles.sql | 1a7d2ceb49b95321c8d41c05d4f846c1dc488a6483716d33a7320bc6871c5609 | 1.0.0 |
| 002_profile_contacts.sql | d8349d00bd1fa9565468c50a7115cdeebf5f2ccdccb48c5c7d4d0d658f3ac874 | 1.0.0 |
| 003_profile_preferences.sql | 73e165dfee118d17c7d0fa1b5ef2131ba066cdddf421e9506b8afbe73f71050a | 1.1.0 |

Only migration 003 was applied; 001 and 002 keep their recorded 1.0.0 digests — nothing
was reimported. Earlier content is intact, and the new migration's content is present:

```
SELECT COUNT(*) AS content_rows FROM cp_customer_profile;    -- 2
SELECT COUNT(*) AS contact_rows FROM cp_profile_contact;     -- 2
SELECT * FROM cp_profile_preference ORDER BY entry;
```

| entry | channel | priority |
| ----- | ------- | -------- |
| 400201 | Email | 1 |
| 400202 | SMS | 2 |
| 400203 | Post | 3 |

**Verdict: PASS.**

## Step 5 — Second realm

Commands (in order):

```
forge realm deploy fixtures/realms/searing-gorge --config /tmp/forge-walk-1/config.yaml --state /tmp/forge-walk-1/state
```

Output (stdout), exit 0:

```
deploying realm from /tmp/forge-walk-1/tree/sql/sql-package-bundle-for-resources/fixtures/realms/searing-gorge
deployed realm searing-gorge (realm id 1)
world database world_searing-gorge, character database character_searing-gorge
port 8085, container realm-searing-gorge, state running
```

```
forge realm deploy fixtures/realms/blackrock-depths --config /tmp/forge-walk-1/config.yaml --state /tmp/forge-walk-1/state
```

Output (stdout), exit 0:

```
deploying realm from /tmp/forge-walk-1/tree/sql/sql-package-bundle-for-resources/fixtures/realms/blackrock-depths
deployed realm blackrock-depths (realm id 2)
world database world_blackrock-depths, character database character_blackrock-depths
port 8086, container realm-blackrock-depths, state running
```

Evidence queries — both realms registered in the one shared authentication database:

```
SELECT id, name, address, port FROM realmlist ORDER BY id;   -- in identity
```

| id | name | address | port |
| -- | ---- | ------- | ---- |
| 1 | Searing Gorge | 127.0.0.1 | 8085 |
| 2 | Blackrock Depths | 127.0.0.1 | 8086 |

Each realm's world database answers what is installed, independently:

```
SELECT name, version, state FROM `world_searing-gorge`.forge_installed_package;
```

| name | version | state |
| ---- | ------- | ----- |
| customer-profiles | 1.0.0 | installed |

```
SELECT name, version, state FROM `world_blackrock-depths`.forge_installed_package;
```

| name | version | state |
| ---- | ------- | ----- |
| customer-profiles | 1.0.0 | installed |

Both records each show 2 applied migrations and 2 content rows in their own
`cp_customer_profile`.

Both containers are running alongside each other, each with its own generated
config and recorded state (`state: running` in each realm's `state.json`):

```
docker inspect -f '{{.Name}} running={{.State.Running}}' realm-searing-gorge realm-blackrock-depths
```

```
/realm-searing-gorge running=true
/realm-blackrock-depths running=true
```

**Verdict: PASS.**

---

## Deviations from the experiment's documents

Found while running, captured verbatim, not fixed (out of scope for this issue).

1. **`forge catalog` does not exist.** TECHNICAL.md, CLI Surface, documents
   `keygen`, `publish`, `verify`, `add`, `trust`, `untrust`, `list`, `discover`,
   `fetch`. `forge --help` lists only `package` and `realm`. Every attempted
   catalog command fails with `Unrecognized command or argument 'catalog'`. This
   is what fails Steps 1 and 2.
2. **Install-by-name does not exist.** TECHNICAL.md documents
   `install <path-or-name>` where "a name resolves through catalogs". The binary's
   argument is `install <path>` — "Install a package from a local directory" — and
   a name is treated as a path (`E_MANIFEST_INVALID`, exit 10). This is what fails
   Step 3's "through the catalog" requirement.
3. **Documented package subcommands are missing.** `search`, `validate`, `hash`,
   `remove` are documented; the binary has only `install`, `upgrade`, `list`.
   `forge package validate fixtures/packages/customer-profiles-v1` →
   `Unrecognized command or argument 'validate'.`
4. **`forge environment` does not exist; the binary says `realm`.** DEPLOY.md,
   Operations, and TECHNICAL.md document `forge environment deploy|start|stop|backup|delete|list`;
   the binary implements `forge realm deploy|stop|start` only. `list` and `backup`
   also fail: `Unrecognized command or argument 'list'.` /
   `Unrecognized command or argument 'backup'.` `forge --help`'s own description
   still says "catalog, package, and environment operations".
5. **Usage errors exit 1, not 2.** TECHNICAL.md, Exit Codes, assigns 2 to usage
   "Produced by System.CommandLine". Every unrecognized-command error observed
   above exited 1.
6. **Config keys differ from the documented schema.** TECHNICAL.md, Configuration,
   documents `spec.environment.configTemplate`, `image`, `basePort`,
   `advertisedAddress`. The binary reads `spec.realm.*` (see
   `src/Forge.Cli/ConfigReader.cs`); the walk config had to use `spec.realm`.
7. **State directory differs.** DEPLOY.md, Derived Names, and TECHNICAL.md, State
   On Disk, document `<state-root>/environments/<name>`. The binary writes
   `<state-root>/realms/<name>` (`src/Forge.Environments/RealmStateStore.cs`).
8. **Documented fixtures are absent.** TECHNICAL.md, Fixtures, lists
   `fixtures/catalogs/valid/`, `fixtures/environments/*`,
   `fixtures/packages/valid-*`, `chain-*`, `cycle-*`, `conflict-*`. None of these
   directories exist in the tree; `fixtures/` holds only `auth/`, `packages/`
   (four packages), and `realms/`.

## Second run from a clean state

Repeated from scratch: fresh MySQL volume (`down -v` / `up -d --wait`), fresh
checkout extracted to `/tmp/forge-walk-2/tree`, empty state dir, re-provisioned
`identity` database. Same commands, same verdicts:

| Step | Run 1 | Run 2 |
| ---- | ----- | ----- |
| 1. Trust | FAIL, exit 1 | FAIL, exit 1 (identical output) |
| 2. Discover | FAIL, exit 1 | FAIL, exit 1 (identical output) |
| 3. Install through the catalog | FAIL, exit 10 | FAIL, exit 10 (identical `E_MANIFEST_INVALID`) |
| 4. Upgrade | PASS | PASS (identical digests and row counts) |
| 5. Second realm | PASS | PASS (both realms registered, both containers running) |

Run 2 details worth pinning:

- After the failed install-by-name, `SHOW TABLES` in `primary` returned nothing —
  the refusal left the database untouched.
- Pre-upgrade digests in Run 2 were identical to Run 1
  (`1a7d2ceb…`, `d8349d00…`), post-upgrade rows identical: 3 applied migrations,
  versions `1.0.0, 1.0.0, 1.1.0`, content counts 2/2/3.
- `realmlist` again held exactly the two rows (id 1 Searing Gorge :8085,
  id 2 Blackrock Depths :8086) and `docker inspect` reported both containers
  running.

## Conclusion

Steps 4 and 5 of the vision's success walk hold end to end. Steps 1 and 2, and
Step 3's catalog path, cannot be walked: the trust/discovery layer exists as
library code and tests but has no surface in the `forge` binary, so the walk
fails at the first step's first command. The five verdicts are: FAIL, FAIL,
FAIL, PASS, PASS — reproducible from a clean state, twice.
