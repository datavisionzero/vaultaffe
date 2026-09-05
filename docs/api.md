# The HTTP API

The HTTP API is the only way into this product and the only way out of it
([Specification §9](../Specification.md#9-technical-guardrails)). The web
application is a client of it exactly as the CLI is; there is no privileged side
door for either.

This file says what a schema cannot: how a client finds out what it is talking
to, what a refusal looks like, and which rules hold across every endpoint. What
each endpoint takes and returns is
[`api/openapi.json`](./api/openapi.json), which is generated from the endpoints
themselves and checked in
([ADR 0006](./adr/0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md)).

**Most of it does not exist yet.** What has landed is the contract itself — the
version in the path, the handshake, the error shape and the document. Identity,
projects, environments and the secrets surface arrive with their own tickets, and
this file is kept accurate as each does.

## Where the endpoints are

```
/api/v1/…            everything, once there is anything
/api/handshake       what this instance is and what it serves
/problems            every refusal this instance can make
/problems/<code>     one of them
/openapi/v1.json     the contract
```

The version is the second segment of every path
([ADR 0005](./adr/0005-the-api-carries-its-version-in-the-path.md)). The four
paths outside it are the ones a client reads *before* it knows whether it and the
instance agree on anything.

## The version exchange

Two versions are in play and they are not the same thing:

| | What it is | Where it comes from |
| --- | --- | --- |
| **Release** | Which build this instance is — `0.4.1`, or `0.0.0-dev` for an untagged one | The tag a release was cut from |
| **Contract version** | Which shape the API has — `v1` | The path, and `info.version` in the document |

`GET /api/handshake` answers both, and needs no token:

```json
{
  "product": "vaultaffe",
  "release": "0.0.0-dev",
  "apiVersions": ["v1"],
  "minimumClient": "0.1.0"
}
```

- `product` is always `vaultaffe`. A client pointed at the wrong host finds that
  out here rather than three requests later.
- `apiVersions` is what this instance serves. A client that needs one not in the
  list is **too new** for this instance and says so — it does not try the request
  and interpret the failure.
- `minimumClient` is the oldest client release this instance still answers. A
  client below it is **too old** and says so.

Beyond the handshake, every request and every response carries a version:

| Header | Direction | What it means |
| --- | --- | --- |
| `Vaultaffe-Client` | request | The client's release, e.g. `0.4.1`. Optional; a browser sends none. |
| `Vaultaffe-Version` | response | This instance's release. On every answer, including refusals and failures. |

A `Vaultaffe-Client` below `minimumClient` is refused with `client-too-old`
before it reaches an endpoint, and one that is not a version at all with
`client-version-unreadable`. That is the whole point of the exchange: being too
old has to arrive as a sentence, not as a field that failed to parse
([§6.3](../Specification.md#63-operations)).

`0.0.0` is never refused for being old. It is what an untagged build calls itself,
and a working copy has nothing to upgrade to.

## Refusals

Every error is `application/problem+json`
([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)):

```json
{
  "type": "/problems/client-too-old",
  "title": "This client is older than this instance accepts",
  "status": 426,
  "detail": "This instance serves clients from 0.1.0 onwards.",
  "code": "client-too-old",
  "minimumClient": "0.1.0"
}
```

**`code` is what a client switches on.** It is the last segment of `type` and
repeats as a member of its own, so that nothing has to take a URI apart to learn
which refusal it is looking at. `title` and `detail` are for a person and are
free to improve; matching on either is a bug waiting for the next wording change.

`type` is relative, which RFC 9457 resolves against the request — so it points at
`/problems/<code>` on the instance that answered, and that path is served. `GET
/problems` lists every code this build can raise, with its status and its title;
an instance is the authority on what it itself refuses.

Some codes carry extra members, as `client-too-old` carries `minimumClient`
above. They are documented with the code.

**A problem document never carries a secret value.** Neither does a log line, and
for the same reason: Specification §6.5 makes a value in either a bug rather than
an untidiness. `detail` says what was refused, not what it was refused about.

### The codes

| Code | Status | When |
| --- | --- | --- |
| `not-found` | 404 | Nothing by that name. |
| `unsupported-api-version` | 404 | The path named a contract version this instance does not serve. Carries `apiVersions`. |
| `client-too-old` | 426 | `Vaultaffe-Client` is below `minimumClient`. Carries `client` and `minimumClient`. |
| `client-version-unreadable` | 400 | `Vaultaffe-Client` is not a version. |
| `internal` | 500 | Something went wrong here. The reason is in this instance's log and deliberately not in the document. |

Only what something already refuses is in that table. Authentication,
authorization and the refusals of the secrets surface arrive with the endpoints
that make them — a code nothing raises is a promise to a client that nothing
keeps.

## The document

`docs/api/openapi.json` is generated from the endpoint definitions and checked
in. Regenerate it with the change that moved it:

```sh
VAULTAFFE_CAPTURE_CONTRACT=1 dotnet test tests/Vaultaffe.IntegrationTests
```

`ContractTests` compares what a running instance serves against the file, and CI
does the same from the other side — it starts the installation, captures the
document and fails on a diff. A response shape that changed without the document
changing with it is red twice.

`info.version` in the document is `v1`, the contract's version, not the
instance's release: the API carries its version in the path, so a release does
not change the document.
