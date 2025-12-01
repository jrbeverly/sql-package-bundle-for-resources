# SQL Package bundle for resources

> [!WARNING]
> **AI-authored:** This change was autonomously planned and implemented by an AI software factory from a human-authored specification, with possible subsequent human review or modification.

Trying to explore a module of distributed SQL files, as a kind of installable artifacts for environments.

| Layer          | Specification          | What it does                                                                                                |
| -------------- | ---------------------- | ----------------------------------------------------------------------------------------------------------- |
| 1. Catalog     | [SPEC.md](SPEC.md)     | Open Artifact Catalog: publisher identity, signed catalogs, artifact digests, discovery, trust              |
| 2. Package     | [SQL.md](SQL.md)       | SQL package format: manifest, ordered migrations, dependency resolution, install tracking                   |
| 3. Environment | [DEPLOY.md](DEPLOY.md) | Environment bundles: database provisioning, config generation, `identity` registration, container lifecycle |

```bash
make setup
make build
make validate

make db-up
FORGE_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=forge;Password=forge-test;Database=primary" \
  make test-integration
make db-down
```

## Notes

- Interesting result from the initial specification, but isn't what was originally in my head
- Sought something lightweight for SQL Bundles for distributing packages
- But I think this is a case of satisfying the specification but missing the intent

Would need to explore alternative direction.

What the proof of concept actually showed, from the five-step success walk ([WALK.md](WALK.md), run twice from a clean state) and the implementation issues (#533–#538, #643–#645):

- The success walk scores FAIL, FAIL, FAIL, PASS, PASS — trust, discover, and install-through-the-catalog fail; upgrade and the second realm pass end to end (WALK.md, Verdicts and Second run).
- The three failures share one cause: the `forge catalog` verb group and install-by-name that TECHNICAL.md documents are not in the binary, which has only `package` and `realm`. Trust and discovery exist as library code and tests, with no executable path to them (WALK.md, Steps 1–3).
- What the binary does implement holds: installation state answered by the database, additive upgrade with nothing reimported, and two bundle-deployed realms sharing one authentication database and nothing else (WALK.md, Steps 3–5).
- Refusal happens before any database contact: the refused install-by-name attempt left `primary` empty (WALK.md, Second run).
- The documents describe more than the binary implements — catalog and environment verbs, package subcommands, exit codes, config keys, state paths, and fixtures — each listed under Specification gaps.
- The PoC implements a restricted slice of the documented formats: SQL.md fields the slice does not use are rejected as unknown, and the tracking tables omit `package_hash` per the PoC cut (#535).
- `POC.md`, the contract the whole tree was to implement, was never written into the tree; its content lives only in issue #530 (reported by #533).
