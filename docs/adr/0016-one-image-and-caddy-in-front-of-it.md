# One Image, and Caddy in Front of It

The Compose stack is **three** services, not four: Postgres, the instance, and
Caddy. The web application is not a service of its own — it is built into the
API's `wwwroot` and served by the same process
([`codebase.md`](../codebase.md)), so one image is the whole product and the
browser reaches the API at its own origin. Caddy is the only thing in front of
it, and the site address is the whole of the TLS decision.

[Specification §6.3](../../Specification.md#63-operations) lists "backend,
frontend, Postgres, and Caddy", which is what the shape looked like before the
frontend existed. What it was asking for is that both halves are in the stack
and that nobody terminates TLS by hand; both hold.

## Why the frontend is not its own container

A second container serving static files has to be **told where the API is**, and
that address is different on a laptop, behind one proxy and behind another. It
is the kind of configuration that is right on the day it is written and wrong
after the first move — and every wrong answer to it looks the same from a
browser: a screen that renders and then cannot ask anything.

Serving the application from the instance removes the question rather than
answering it. There is no origin to configure, no CORS to allow, no second place
a released version can drift from the first, and no case where the application a
person is looking at was cut from a different build than the API it is talking
to. It is also not a second read path
([Specification §9](../../Specification.md#9-technical-guardrails)): static files
are not data, and the application asks the same public API the CLI does for
every byte of the product's own.

The price is real and small at this size. The image is rebuilt when either half
changes, the two cannot be scaled apart, and a CDN in front of the SPA is not
something this shape offers. A secrets manager for a team that fits in one
Postgres has no use for any of the three.

## Why Caddy, and why one variable

TLS is part of "in minutes"
([§6.3](../../Specification.md#63-operations)): a token over plain HTTP is a
token in a network log. Caddy obtains and renews a certificate for a domain on
its own, which is the only way that sentence survives contact with somebody
setting an instance up on a Friday — a proxy that needs a certificate placed by
hand turns HTTPS into the step after the first secret rather than before it.

So there is one variable, `VAULTAFFE_SITE_ADDRESS`, and it has two cases. A
domain means a real certificate. The default `:80` means plain HTTP for whoever
asked, which is the trial on a laptop and is safe **because the CLI refuses
plain HTTP off loopback unless it is told otherwise** (`docs/cli.md`): the
insecure case is reachable at `localhost` and nowhere else by accident.

## What is deliberately not configured

**Nothing about the proxy in front.** No `X-Forwarded-*` trust, no address of a
trusted network. The instance builds no absolute URL — an invitation link is
made in the browser it will be pasted from
([ADR 0015](./0015-an-invitation-is-a-credential-in-a-link.md)) — it sets no
cookie ([ADR 0014](./0014-the-browser-holds-its-session-for-as-long-as-the-tab.md)),
and it records no caller address: the change log holds an identity and its type
([§6.5](../../Specification.md#65-logging-and-history)) and no network. So there
is nothing whose correctness depends on the caller's scheme or address, and a
trust setting for it now would be a knob that could only be wrong. When
something needs one, that is the commit it arrives in.
