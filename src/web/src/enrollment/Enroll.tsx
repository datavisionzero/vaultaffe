import { useSearchParams } from "react-router";
import { useState } from "react";
import { api, describe, type Project } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import { Field, Refusal } from "@/shared/Form";
import { around, when } from "@/shared/moments";
import { Rows } from "@/shared/Rows";
import { TokenFields } from "@/settings/TokenFields";
import { bindingsOf, scopes, type Draft } from "@/settings/tokenDraft";

/** What the instance says about the machine that asked. */
type Request = {
  requestedName: string;
  askedAt: string;
  expiresAt: string;
  state: string;
};

/**
 * `/enroll` — a machine has asked for a token of its own, and this is where a
 * person decides
 * ([ADR 0021](../../../../docs/adr/0021-an-agent-asks-for-its-own-token.md)).
 *
 * **What this screen exists to avoid is a copy and a paste.** Creating a token
 * prints its value, and a value that has been printed has been in a terminal, a
 * scrollback and — where an agent ran the command — a transcript. Nothing on
 * this screen is worth anything to whoever reads it over somebody's shoulder:
 * the short code the person types needs their session to mean anything, and the
 * value goes to the machine that asked, straight into a file, without passing
 * through a person at all.
 *
 * **The name is a claim and this screen says so.** An instance cannot tell
 * whether a machine calling itself an agent on somebody's laptop is one; what it
 * can do is refuse to dress the claim up as a fact. So the requested name is
 * shown as what was asked, the person confirms or replaces it, and what they
 * decide is what the token is called from then on.
 *
 * The default is the whole organization with every scope, because the point of
 * an agent token is attribution and not restriction
 * ([§6.4](../../../../Specification.md#64-permissions-in-the-mvp)) — and
 * narrowing it here is exactly the same form the tokens screen uses, so that
 * what a person may agree to and what they may issue can never come apart.
 */
export function Enroll() {
  const [parameters] = useSearchParams();
  const typed = parameters.get("code") ?? "";

  const [code, setCode] = useState(typed);
  const [asking, setAsking] = useState(typed !== "");

  return (
    <div className="mx-auto grid w-full max-w-lg gap-6">
      <div>
        <h1 className="text-lg font-semibold">A machine is asking for a token</h1>
        <p className="max-w-prose text-sm text-muted-foreground">
          Something running somewhere — an agent in a terminal, a job on a build machine — has
          asked this instance for a credential of its own, and printed a code. Type it here to see
          what asked and to decide what it may do.
        </p>
      </div>

      {asking ? (
        <Decide code={code} onDone={() => setAsking(false)} />
      ) : (
        <form
          className="grid gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            setAsking(code.trim() !== "");
          }}
        >
          <Field
            label="The code"
            required
            autoFocus
            value={code}
            onChange={(event) => setCode(event.target.value)}
            hint="Eight letters, as the machine printed them — the dash and the case do not matter."
            className="font-mono"
          />
          <div>
            <Button type="submit" disabled={code.trim() === ""}>
              Look it up
            </Button>
          </div>
        </form>
      )}
    </div>
  );
}

/** What was asked, and the decision. */
function Decide({ code, onDone }: { code: string; onDone: () => void }) {
  const [asked, again] = useAsk<Request>(`enrollment:${code}`, () =>
    api.GET("/api/v1/enrollments/{code}", { params: { path: { code } } }),
  );

  const [projects] = useAsk<Project[]>("enrollment:projects", () => api.GET("/api/v1/projects"));

  const catalogue = projects.at === "answered" ? projects.data : [];

  if (asked.at === "asking") {
    return <Rows count={3} />;
  }

  if (asked.at === "refused") {
    return (
      <div className="grid gap-3">
        <Refusal>{asked.why}</Refusal>
        <div>
          <Button variant="outline" onClick={onDone}>
            Try another code
          </Button>
        </div>
      </div>
    );
  }

  return (
    <Decision
      code={code}
      asked={asked.data}
      catalogue={catalogue}
      onDecided={again}
      onDone={onDone}
    />
  );
}

function Decision({
  code,
  asked,
  catalogue,
  onDecided,
  onDone,
}: {
  code: string;
  asked: Request;
  catalogue: Project[];
  onDecided: () => void;
  onDone: () => void;
}) {
  const [draft, setDraft] = useState<Draft>({
    name: asked.requestedName,
    scopes: [...scopes],
    reach: "organization",
    picked: [],
  });
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();

  // Anything but "pending" is a decision somebody already took, or a code that
  // ran out while this page was open. Saying which is the whole of what is left
  // to say: there is nothing here to decide any more.
  if (asked.state !== "pending") {
    return (
      <div className="grid gap-3 rounded-lg border p-4">
        <p className="text-sm">
          {asked.state === "approved" &&
            "Somebody has already agreed to this one. The machine collects its token on its next poll."}
          {asked.state === "redeemed" &&
            "This one is done: the machine has collected its token. It is in the tokens list, where it can be changed or revoked."}
          {asked.state === "denied" && "Somebody has already refused this one."}
          {asked.state === "expired" &&
            "Nobody decided this one in time. Whatever asked can ask again; a code is good for ten minutes so that an abandoned one is not lying around all afternoon."}
        </p>
        <div>
          <Button variant="outline" onClick={onDone}>
            Done
          </Button>
        </div>
      </div>
    );
  }

  async function decide(approve: boolean) {
    setBusy(true);
    setRefusal(undefined);

    try {
      const { error, response } = approve
        ? await api.POST("/api/v1/enrollments/{code}/approval", {
            params: { path: { code } },
            body: {
              name: draft.name,
              scopes: draft.scopes,
              bindings: bindingsOf(catalogue, draft),
            },
          })
        : await api.POST("/api/v1/enrollments/{code}/refusal", {
            params: { path: { code } },
          });

      if (error !== undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      onDecided();
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="grid gap-5" onSubmit={(event) => event.preventDefault()}>
      <div className="grid gap-1 rounded-lg border p-3">
        <p className="text-sm">
          It calls itself <span className="font-medium">{asked.requestedName}</span>.
        </p>
        <p className="text-xs text-muted-foreground">
          That is what it said about itself, and this instance has no way to check it. Agree only
          if you know what asked — you started it, or somebody you can ask did.
          <span title={when(asked.askedAt)}> It asked {around(asked.askedAt)}</span>
          <span title={when(asked.expiresAt)}> and the code runs out {around(asked.expiresAt)}</span>
          .
        </p>
      </div>

      <TokenFields
        catalogue={catalogue}
        draft={draft}
        change={(part) => setDraft((current) => ({ ...current, ...part }))}
        hint="What a revocation list will call it, months from now. The machine suggested this; you decide it."
      />

      <p className="text-xs text-muted-foreground">
        The value goes to the machine that asked and is never shown here. Nothing on this screen
        has to be copied anywhere.
      </p>

      {refusal !== undefined && <Refusal>{refusal}</Refusal>}

      <div className="flex flex-wrap gap-2">
        <Button
          disabled={busy || draft.name.trim() === "" || draft.scopes.length === 0}
          onClick={() => void decide(true)}
        >
          {busy ? "Working…" : "Agree, and hand it the token"}
        </Button>
        <Button variant="outline" disabled={busy} onClick={() => void decide(false)}>
          No, I did not start this
        </Button>
      </div>
    </form>
  );
}
