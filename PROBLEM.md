# Problem

## Overview

Database content for application deployments is distributed as loose SQL
files and full database dumps passed between database operators. There is no
common format for describing what a change contains, no record of what has been
applied to a database, and no way to establish who produced a file or whether it
arrived intact.

An operator who wants to run a modified primary today applies SQL by hand, keeps
notes outside the database, and rebuilds from scratch when an upgrade goes
wrong. A creator who wants to distribute content has no way to ship it other
than a forum attachment.

This repository addresses the packaging, distribution, and deployment gap around
that content. It does not change how the content itself is written: standard SQL
remains the implementation language.

---

# Core Problem Areas

## No Unit of Distribution

A change to a primary database has no boundary. A creator's work exists as a pile
of `INSERT` statements with no name, version, author, license, or statement of
what core and database version it targets.

Consequences:

- Two changes cannot be told apart once applied.
- A change cannot be described to a tool, only to a human.
- Nothing can depend on anything else, because there is nothing to depend on.

## No Installation State

The target database records nothing about what was applied to it. The database
is the only state, and it is a merged result rather than a history.

Consequences:

- An operator cannot answer "what is installed here".
- An upgrade cannot know what it is upgrading from.
- A failed partial application leaves no marker saying where it stopped.
- Two operators with the same intent end up with different databases.

## No Provenance Or Integrity

A SQL file downloaded from a forum has no author binding and no integrity check.
An operator executing it is granting arbitrary write access to their primary
database on the strength of a filename.

Consequences:

- A modified file is indistinguishable from the original.
- Identity is tied to hosting: the same creator on a new host is a new stranger.
- There is no way to keep trusting a creator after they move.

## No Discovery Without Centralisation

The obvious fix for discovery is a central registry, which introduces an owner,
a hosting bill, a moderation policy, and a single point of failure for an
ecosystem that is otherwise fully decentralised.

Consequences:

- Discovery either does not exist or belongs to whoever runs the registry.
- Creators cannot publish without permission.
- The registry becomes the trust anchor whether or not that was intended.

## Manual, Unrepeatable Deployment

Standing up an environment means creating databases, importing dumps, hand-editing
`application.conf`, inserting a row into the `identity` database, and starting a
process. Each environment is configured once, by hand, and the configuration exists
only on that machine.

Consequences:

- An environment cannot be rebuilt from a description.
- Running several environments means repeating the work and keeping the differences
  straight by memory.
- Experimenting is expensive, so operators do not experiment.

## Upgrades Are Destructive

Without installation state or a defined migration order, upgrading content means
reimporting a dump, which discards everything applied on top of it.

Consequences:

- Content from two creators cannot coexist across an upgrade.
- Operators pin to old versions rather than risk the upgrade.
- Rollback means restoring a backup of the whole database.

---

# Constraints

## Hard Constraints

These cannot be relaxed. A design that violates one is wrong, not a trade-off.

- **No modifications to the target application.** No patches, no forks, no required
  upstream changes. The core is consumed as released.
- **Standard SQL stays the implementation language.** No new DSL, no templating
  layer, no preprocessor. A migration file must be executable by `mysql`
  directly.
- **Existing SQL remains valid.** Content that works today must work inside a
  package with no rewriting beyond being placed in a file.
- **No central registry.** No component may require a service that a single
  party operates. Publishing must work from static file hosting.
- **Discovery must not confer trust.** Finding a publisher and trusting a
  publisher are separate acts, and the second requires explicit operator
  action.
- **Nothing downloaded is executed as code.** Artifacts are data. SQL is
  executed against a database by the operator's instruction, never implicitly
  during discovery or download.

## Operational Constraints

- MySQL-compatible server, reached over a normal client connection. No
  filesystem access to the database host.
- DDL in MySQL is not transactional, so a failed migration cannot be rolled
  back by the database. Recovery is forward-only.
- The operator owns credentials. The tooling does not manage secrets or create
  database users beyond what an install requires.
- Containers are an implementation detail of environment deployment, not a dependency
  of the package or catalog layers.

## Scope Constraints

- This is an experiment in this monorepo, not maintained software. It exists to
  observe whether the three layers compose, and it may stop working as
  surrounding technologies move.
- The target is one operator managing their own environments, not a hosting platform
  with tenants.
- Content quality, data compatibility, and licensing of distributed content are the
  publisher's concern, not the tooling's.
