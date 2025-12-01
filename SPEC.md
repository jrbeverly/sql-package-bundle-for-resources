# Open Artifact Catalog (OAC)

**Version:** 1.0
**Status:** Normative for Phase 2 of this repository
**Layer:** 1 of 3 — see [README.md](README.md)

A decentralised discovery protocol for immutable software artifacts.

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY are to be interpreted
as described in RFC 2119.

## Scope

OAC defines:

- publisher identity
- signed catalogs
- artifact metadata
- discovery of further catalogs
- multiple download transports
- the verification a client performs

OAC deliberately does **not** define:

- installation
- dependency resolution
- package management
- execution
- update policy

Those belong to the layer above. In this repository that layer is
[SQL.md](SQL.md); an OAC client has no knowledge of it.

---

# Core Concepts

There are four objects and one local database.

```
Publisher ──owns──> Catalog ──contains──> Artifact ──has──> Sources
                       │
                       └──references──> other Catalogs

Trust Database   local, per client, never transmitted
```

---

# Publisher

A publisher is an identity that owns catalogs. The public key is the identity;
the id and display name are labels for humans.

```json
{
    "id": "jon",
    "displayName": "Jon's Tools",
    "publicKey": {
        "type": "ed25519",
        "key": "kK7k2mVl0t3oQ8n0Xk1Gm5rZ4xW9cB2vH6sT1yU3pA0="
    }
}
```

| Field | Type | Required | Notes |
| ----- | ---- | -------- | ----- |
| `id` | string | yes | `[a-z0-9][a-z0-9-]{0,63}`. A label. NOT an identity and NOT unique across publishers. |
| `displayName` | string | yes | Free text, 1–128 characters. Presentation only. |
| `publicKey.type` | string | yes | `ed25519`. A client MUST reject types it does not implement. |
| `publicKey.key` | string | yes | Base64 (RFC 4648 §4, with padding) of the raw 32-byte Ed25519 public key. |

Rules:

- A client MUST treat `publicKey.key` as the publisher's identity for every
  comparison, cache key, and trust decision.
- A client MUST NOT treat `id` or `displayName` as identifying. Two unrelated
  publishers may both use `id` `jon`.
- Changing where a catalog is hosted MUST NOT change the publisher's identity.
- A client MUST NOT silently accept a different `publicKey.key` for an
  already-trusted publisher. See [Key Changes](#key-changes).

---

# Catalog

A catalog is a signed JSON document. The recommended filename is `index.json`.

```json
{
    "specVersion": 1,
    "publisher": {
        "id": "jon",
        "displayName": "Jon's Tools",
        "publicKey": { "type": "ed25519", "key": "..." }
    },
    "version": 42,
    "created": "2026-07-18T00:00:00Z",
    "expires": "2026-08-18T00:00:00Z",
    "artifacts": [],
    "catalogs": [],
    "signature": {
        "algorithm": "ed25519",
        "value": "..."
    }
}
```

| Field | Type | Required | Notes |
| ----- | ---- | -------- | ----- |
| `specVersion` | integer | yes | `1` for this document. A client MUST reject a major version it does not implement (`E_SPEC_VERSION_UNSUPPORTED`). |
| `publisher` | object | yes | See [Publisher](#publisher). |
| `version` | integer | yes | Monotonically increasing per publisher. See [Rollback Protection](#rollback-protection). |
| `created` | string | yes | RFC 3339 timestamp, UTC, `Z` suffix. |
| `expires` | string | no | RFC 3339 timestamp, UTC. Absent means the client decides its own cache lifetime. |
| `artifacts` | array | yes | May be empty. See [Artifact](#artifact). |
| `catalogs` | array | yes | May be empty. See [Catalog Discovery](#catalog-discovery). |
| `signature` | object | yes | See [Signing](#signing). |

A client MUST preserve unknown fields verbatim when storing or re-serialising a
catalog. See [Extensibility](#extensibility).

## Signing

The signature covers the canonical serialisation of the catalog document with
the `signature` member removed from the top-level object.

Procedure, for both signing and verification:

1. Parse the document as JSON.
2. Remove the top-level `signature` member. Remove nothing else — every other
   member, including unknown ones, is covered.
3. Serialise the result with JSON Canonicalization Scheme (JCS), RFC 8785.
4. Take the UTF-8 bytes of that serialisation as the signing input.
5. Sign or verify with Ed25519 (RFC 8032) using `publisher.publicKey.key`.

| Field | Type | Required | Notes |
| ----- | ---- | -------- | ----- |
| `signature.algorithm` | string | yes | `ed25519`. MUST match `publisher.publicKey.type`. |
| `signature.value` | string | yes | Base64 (RFC 4648 §4, with padding) of the raw 64-byte signature. |

Rules:

- A client MUST verify the signature before reading `artifacts` or `catalogs`.
  Nothing in an unverified catalog may influence behaviour, including limit
  accounting and cache writes.
- A client MUST reject a catalog it cannot canonicalise
  (`E_CANONICALIZATION_FAILED`) rather than falling back to signing the raw
  bytes.
- A client MUST reject a signature that does not verify
  (`E_SIGNATURE_INVALID`) and MUST NOT retry against a different key.
- The key used for verification MUST come from the catalog's own `publisher`
  block, and that block MUST then be compared against the trust database. A
  self-consistent catalog proves only that its author holds the key it names.

## Rollback Protection

A client MUST record the highest `version` it has accepted for each publisher
key. A catalog whose `version` is lower than that recorded value MUST be
rejected (`E_CATALOG_ROLLBACK`), even when its signature verifies.

A catalog whose `version` equals the recorded value MAY be accepted as
unchanged.

## Expiry

When `expires` is present and in the past, a client MUST reject the catalog
(`E_CATALOG_EXPIRED`) rather than serving its contents as stale. A client MAY
offer an explicit operator override; it MUST NOT apply one by default.

---

# Artifact

Artifacts are immutable. If the bytes change, the digest changes, and it is a
different artifact.

```json
{
    "id": "customer-profiles",
    "version": "1.2.0",
    "digest": {
        "algorithm": "sha256",
        "value": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"
    },
    "size": 1538429,
    "mediaType": "application/vnd.oac.sqlpackage.v1+zip",
    "description": "Customer profile resources",
    "tags": ["sql", "content"],
    "sources": []
}
```

| Field | Type | Required | Notes |
| ----- | ---- | -------- | ----- |
| `id` | string | yes | `[a-z0-9][a-z0-9-]{0,63}`. Unique within the publisher, not globally. |
| `version` | string | yes | Semantic Versioning 2.0.0. |
| `digest.algorithm` | string | yes | `sha256`. A client MUST reject algorithms it does not implement. |
| `digest.value` | string | yes | Lowercase hex, 64 characters for `sha256`. |
| `size` | integer | yes | Exact byte length. A client MUST abort a download that exceeds it. |
| `mediaType` | string | no | Tells the layer above what the bytes are. An OAC client does not interpret it. |
| `description` | string | no | Free text, presentation only. |
| `tags` | array of string | no | Free text, presentation and filtering only. |
| `sources` | array | yes | At least one entry. See [Sources](#sources). |

---

# Sources

An artifact may be available from several locations. Sources are hints about
where bytes can be found; they carry no authority.

```json
"sources": [
    {
        "type": "https",
        "url": "https://example.com/packages/customer-profiles-1.2.0.zip"
    },
    {
        "type": "oci",
        "reference": "ghcr.io/jon/customer-profiles:v1.2.0"
    }
]
```

| Source type | Required field | Notes |
| ----------- | -------------- | ----- |
| `https` | `url` | Scheme MUST be `https`. A client MUST reject any other scheme (`E_SCHEME_UNSUPPORTED`), `http` included. |
| `oci` | `reference` | A registry reference. OCI is one transport among many, not a discovery mechanism. |

Rules:

- A client MAY use whichever source types it implements and MUST skip the rest
  rather than failing the artifact.
- A client MUST verify `digest` after every download, whatever the source
  (`E_DIGEST_MISMATCH`), and MUST discard the bytes on mismatch.
- A client MUST NOT treat a source host as trusted, and MUST NOT weaken digest
  verification because a source looks authoritative.
- When several sources are listed, a client MAY try the next one after a
  failure. A digest mismatch is a failure of the artifact, not of the source: a
  client MAY try another source, and MUST NOT report success unless some source
  produced bytes matching the digest.

---

# Publishing

The publisher side of the protocol. A publisher needs a keypair, somewhere to
host bytes, and somewhere to host one JSON file.

Procedure for building a catalog from a directory of artifacts:

1. Read a publisher description: `id`, `displayName`, and the private key.
2. For each file in the directory, create an artifact entry:
   - `id` and `version` come from the publisher's own naming, supplied
     alongside the file rather than parsed out of the filename. Filename parsing
     is not specified here and a publisher tool MUST NOT guess.
   - `digest` is the SHA-256 of the file's bytes.
   - `size` is the file's byte length.
   - `mediaType` is supplied by the publisher. See the binding in
     [SQL.md](SQL.md) for the value SQL packages use.
   - `sources` are supplied by the publisher — the URLs the bytes will be
     reachable at. A publisher tool MUST NOT invent a URL from a local path.
3. Set `version` to one greater than the previously published catalog's
   `version`, or `1` for a first catalog.
4. Set `created` to now, and `expires` if the publisher configured a lifetime.
5. Carry `catalogs` forward from the publisher's configured recommendations.
6. Canonicalise and sign per [Signing](#signing), then insert `signature`.

Rules:

- A publisher MUST NOT reuse a `version` with different content. A client
  enforcing rollback protection will accept the first document it sees at that
  version and reject the second.
- A publisher tool MUST verify its own output before writing it, using the same
  verification path a client uses. A catalog that fails its author's verifier is
  never worth publishing.
- The private key MUST be written and read with owner-only permissions and MUST
  NOT appear in a catalog, in output, or in an error message.

Publishing requires no service. A signed `index.json` on static hosting, and the
artifact bytes anywhere reachable over HTTPS, is a complete publication.

---

# Catalog Discovery

A catalog may recommend other catalogs.

```json
"catalogs": [
    {
        "url": "https://alice.example.com/index.json",
        "publisher": "alice",
        "relationship": "recommended"
    }
]
```

| Field | Type | Required | Notes |
| ----- | ---- | -------- | ----- |
| `url` | string | yes | `https` only (`E_SCHEME_UNSUPPORTED`). |
| `publisher` | string | no | The expected publisher `id`. A label; it proves nothing and MUST NOT be used as identity. |
| `relationship` | string | no | `recommended` or `mirror`. Unknown values are treated as `recommended`. |

Rules:

- These are recommendations. A client MUST NOT automatically trust a discovered
  publisher.
- A client MUST NOT download an artifact from a discovered catalog whose
  publisher is untrusted (`E_PUBLISHER_UNTRUSTED`).
- A client MAY fetch and verify a discovered catalog in order to show the
  operator who the publisher is. Fetching is not trusting.

---

# Trust Model

Each client keeps its own trust database. It is local, operator-owned, and never
transmitted.

```
Trusted Publishers

  ✓ Jon      kK7k2mVl0t3oQ8n0Xk1Gm5rZ4xW9cB2vH6sT1yU3pA0=   version seen: 42
  ✓ Alice    3pA0kK7k2mVl0t3oQ8n0Xk1Gm5rZ4xW9cB2vH6sT1yU=   version seen: 7
```

A trust entry MUST hold at least the public key, the algorithm, the display name
last seen, and the highest catalog version accepted. The key is the primary key.

Rules:

- Trust requires an explicit operator action. A client MUST NOT enrol a
  publisher as a side effect of discovery, download, or configuration import.
- Before asking the operator to trust a publisher, a client MUST show the
  publisher's display name, full public key, and the catalog URL it came from.
- Trust MUST NOT propagate. A trusted publisher's recommendations are
  suggestions about publishers, never grants.
- Untrusting a publisher MUST NOT delete already-installed artifacts; it stops
  future acceptance.

## Key Changes

If a catalog presents a publisher `id` that matches a trusted entry but a
different `publicKey.key`, the client MUST reject the catalog
(`E_KEY_CHANGED`), report both keys, and require an explicit operator action to
replace the trusted key. A client MUST NOT resolve this by trusting both keys,
by preferring the newer one, or by matching on `id`.

This is a rotation and an attack seen from the same angle, and the protocol
cannot tell them apart. Only the operator can.

---

# Communities

A community is a named bundle of publishers, distributed as a file an operator
imports.

```json
{
    "specVersion": 1,
    "name": "Open Source Graphics",
    "publishers": [
        { "id": "jon",  "displayName": "Jon's Tools", "publicKey": { "type": "ed25519", "key": "..." } },
        { "id": "sam",  "displayName": "Sam's Maps",  "publicKey": { "type": "ed25519", "key": "..." } }
    ]
}
```

Importing a community is one operator action that proposes every listed
publisher for trust. A client MUST show the full list, with keys, before
enrolling any of them, and MUST let the operator enrol a subset.

Communities are recommendations, not authorities. A community file is not signed
by the publishers it lists and proves nothing about them.

---

# Discovery Flow

```
Operator adds catalog URL
            │
            ▼
   Download index.json          ── enforce size and timeout limits
            │
            ▼
   Canonicalise and verify      ── E_SIGNATURE_INVALID, E_CANONICALIZATION_FAILED
            │
            ▼
   Check specVersion            ── E_SPEC_VERSION_UNSUPPORTED
   Check expiry                 ── E_CATALOG_EXPIRED
   Check version not lower      ── E_CATALOG_ROLLBACK
            │
            ▼
   Publisher key in trust db?
            │
     ┌──────┴──────┐
     │ no          │ yes
     ▼             ▼
  Show key,     Read artifacts
  name, URL.    Read recommended catalogs
  Ask operator. Display recommendations
  Stop here     Record highest version seen
  unless
  trusted.
```

No recursive trust. No automatic installation. Verification precedes
interpretation at every step.

---

# Download Flow

```
Operator selects artifact (publisher + id + version)
            │
            ▼
Publisher trusted?  ── no ──> E_PUBLISHER_UNTRUSTED
            │ yes
            ▼
Choose a source whose type the client implements
            │
            ▼
Download, aborting past `size` or the byte limit
            │
            ▼
Compute sha256 over the received bytes
            │
            ▼
Equal to digest.value?  ── no ──> discard bytes, E_DIGEST_MISMATCH
            │ yes
            ▼
Hand bytes to the caller. Do not execute them.
```

No source is trusted. Only the digest is.

---

# Recursive Discovery

A client walking recommended catalogs MUST track, for one walk:

- visited URLs, normalised, to detect cycles (`E_CYCLE_DETECTED`)
- visited publisher keys
- current recursion depth
- total catalogs fetched
- total bytes fetched

Exceeding any limit MUST stop the walk with `E_LIMIT_EXCEEDED` and MUST NOT
partially enrol anything discovered so far.

| Limit | Default |
| ----- | ------- |
| Maximum recursion depth | 5 |
| Maximum catalogs per walk | 250 |
| Maximum catalog document size | 10 MB |
| Maximum artifacts per catalog | 10,000 |
| Maximum HTTP redirects | 5 |
| Request timeout | 30 seconds |

A client MUST enforce the document size limit while reading the response, not
after buffering it. A client MUST re-check the scheme after each redirect.

Limits are defaults, not protocol constants. A client MAY let the operator
change them and MUST apply some finite limit for each.

---

# Caching

A client MAY cache a catalog until `expires`. A client MAY additionally use HTTP
`ETag`, `Last-Modified`, and `Cache-Control` when the source provides them.

A cached catalog MUST have been verified before it was cached, and its
`version` MUST still pass rollback protection when read back. A client MUST NOT
cache a catalog that failed any check.

---

# Conflict Resolution

An artifact is identified by three things together:

```
publisher public key  +  artifact id  +  version
```

not by `id` alone, and not by publisher `id`.

Two publishers may both publish `editor` at `1.0.0` with different bytes, and
both are valid. A client MUST keep them distinct in storage, in display, and in
any resolution it performs.

---

# Error Conditions

Every rejection MUST map to one of these. Implementations use these names
directly; see the exit-code table in [TECHNICAL.md](TECHNICAL.md).

| Code | Meaning |
| ---- | ------- |
| `E_SPEC_VERSION_UNSUPPORTED` | `specVersion` is a major version the client does not implement. |
| `E_CANONICALIZATION_FAILED` | The document cannot be canonicalised per RFC 8785. |
| `E_SIGNATURE_INVALID` | Signature does not verify against the catalog's own publisher key. |
| `E_CATALOG_EXPIRED` | `expires` is in the past. |
| `E_CATALOG_ROLLBACK` | `version` is lower than the highest accepted for this key. |
| `E_KEY_CHANGED` | Publisher `id` matches a trusted entry with a different key. |
| `E_PUBLISHER_UNTRUSTED` | An operation required trust that has not been granted. |
| `E_DIGEST_MISMATCH` | Downloaded bytes do not match the artifact digest. |
| `E_SCHEME_UNSUPPORTED` | A URL used a scheme other than `https`. |
| `E_LIMIT_EXCEEDED` | A discovery, size, or count limit was reached. |
| `E_CYCLE_DETECTED` | A catalog reference cycle was found during a walk. |

---

# Security Requirements

A client MUST:

- verify signatures before interpreting any catalog content
- verify artifact digests after every download, from every source
- treat the public key as identity and refuse silent key replacement
- require explicit operator action before trusting any publisher
- detect reference cycles and enforce every limit
- reject URL schemes other than `https`
- enforce rollback protection per publisher key
- preserve unknown JSON fields

A client MUST NOT:

- execute downloaded artifacts
- automatically trust discovered publishers or imported communities
- silently replace a trusted publisher key
- continue after a verification failure
- treat a source, host, or TLS certificate as a substitute for a digest
- let an unverified catalog influence cache state or limit accounting

---

# Extensibility

A client MUST ignore fields it does not recognise, and MUST preserve them when
storing or re-serialising a document.

```json
{
    "id": "tool",
    "version": "1.0.0",
    "digest": { "algorithm": "sha256", "value": "..." },
    "size": 1024,
    "sources": [],
    "someFutureExtension": { "anything": true }
}
```

Preservation matters because unknown fields are covered by the signature.
Dropping one and re-serialising produces a document whose signature no longer
verifies, which looks identical to tampering. This follows TUF's guidance that
implementations preserve unknown fields when verifying and reserialising
metadata.

A client MUST NOT let an unknown field change behaviour. An extension that must
be understood to be safe requires a `specVersion` increment instead.

---

# Design Philosophy

OAC separates four concerns that are usually combined:

- **Identity** is a cryptographic public key.
- **Discovery** is signed catalogs recommending other catalogs.
- **Distribution** is any transport that serves immutable bytes — HTTPS, OCI
  registries, object storage, mirrors.
- **Verification** is signatures over catalogs and digests over artifacts.

The result is an ecosystem with no central registry, no mandatory hosting
provider, and no single authority. A publisher participates by hosting a static
`index.json`, signing it, and putting immutable bytes somewhere reachable. A
client retains complete control over which publishers it trusts, and trust never
propagates through the discovery graph.

The deliberate cost of this design is that discovery is worth less than it would
be with a registry: an operator must decide about each publisher. That cost is
the point — it is what keeps discovery from becoming enrolment.

---

# References

- RFC 2119 — Key words for use in RFCs
- RFC 3339 — Date and Time on the Internet: Timestamps
- RFC 4648 — The Base16, Base32, and Base64 Data Encodings
- RFC 8032 — Edwards-Curve Digital Signature Algorithm (EdDSA)
- RFC 8785 — JSON Canonicalization Scheme (JCS)
- [The Update Framework — Roles and metadata](https://theupdateframework.com/docs/metadata/)
- [TUF specification](https://github.com/theupdateframework/specification/blob/master/tuf-spec.md)
- [ORAS artifacts specification](https://github.com/oras-project/artifacts-spec)
