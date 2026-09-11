import { PlusIcon } from "lucide-react";
import { useState, type FormEvent, type ReactNode } from "react";
import { answered, api, describe, type Project, type Token } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useSession } from "@/session/useSession";
import { ActionDialog } from "@/shared/ActionDialog";
import { Copyable } from "@/shared/Copyable";
import { Refusal } from "@/shared/Form";
import { around, when } from "@/shared/moments";
import { Rows } from "@/shared/Rows";
import { Choice, TokenFields } from "./TokenFields";
import { bindingsOf, draftOf, scopes, type Draft } from "./tokenDraft";


/**
 * `/settings/tokens` — the organization's tokens with kind, name, scopes,
 * binding and standing; creating one; and the value of a new one, once
 * (`docs/human-interface.md`).
 *
 * **Two lists and not one.** A token an agent or a service acts under is created
 * deliberately, carries a name a person chose, and there are a few of them; a
 * session is what every sign-in leaves behind, has no name, and expires. The
 * question each list answers differs as much: "which standing credentials exist,
 * and how far does each reach" is an inventory, and "where am I signed in, and is
 * one of these not mine" is a question about devices — which is also why one is
 * ordered by name and the other by when it appeared. Mixed into a single list the
 * sessions would, by their number alone, push the few credentials that matter off
 * the screen. Creating belongs to the first list for the same reason: this screen
 * issues an agent or a service token and never a session.
 *
 * **Everything but listing is a person's** and the instance says so: a token is
 * itself a secret, and one an agent created through the CLI would be printed to
 * its own stdout and into its context
 * ([Specification §6.1](../../../../Specification.md#61-web-ui)). Listing is not
 * on that list — a revocation list an agent cannot read is not one.
 *
 * The default for an agent token is **the whole organization with every scope**,
 * because the point is attribution and not restriction, and the first narrowing
 * offered is keeping it out of production
 * ([§6.4](../../../../Specification.md#64-permissions-in-the-mvp)).
 *
 * **A row has three acts, and they are three because the value is the thing that
 * cannot change.** Changing arranges a name, a scope set and a reach without
 * touching the value, so whatever holds the token keeps working — this is the
 * screen's answer to a credential that has to reach one more project, and it is
 * why nobody has to go round every machine holding it. Revoking stops the value
 * and leaves the row. Deleting takes the row, and only a revoked one is offered
 * it: a revocation list that let something vanish while it still worked would be
 * the one list nobody could trust.
 */
export function Tokens() {
  const [revoked, setRevoked] = useState(false);

  const [tokens, again] = useAsk<Token[]>(revoked ? "tokens:revoked" : "tokens", () =>
    api.GET("/api/v1/tokens", { params: { query: { revoked } } }),
  );
  const [projects] = useAsk<Project[]>("tokens:projects", () => api.GET("/api/v1/projects"));

  const catalogue = projects.at === "answered" ? projects.data : [];
  const all = tokens.at === "answered" ? tokens.data : [];

  // `filter` hands back a new array, so sorting it leaves what the ask holds
  // alone.
  const issued = all
    .filter((token) => token.kind !== "session")
    .sort((a, b) => inUseFirst(a, b) || (a.name ?? "").localeCompare(b.name ?? ""));

  const sessions = all
    .filter((token) => token.kind === "session")
    .sort(
      (a, b) =>
        inUseFirst(a, b) || new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
    );

  // One refusal for one ask: both lists come from the same request, and saying
  // it twice would suggest two things went wrong.
  if (tokens.at === "refused") {
    return <Refusal>{tokens.why}</Refusal>;
  }

  return (
    <div className="grid gap-6">
      <Section
        title="Tokens"
        what="What an agent or a service acts under, so the change log can say what kind of thing acted. Named, and never listed with its value."
        action={
          <div className="flex items-center gap-3">
            <ShowRevoked shown={revoked} onChange={setRevoked} />
            <NewToken catalogue={catalogue} onCreated={again} />
          </div>
        }
      >
        {tokens.at === "asking" ? (
          <Rows count={3} />
        ) : issued.length === 0 ? (
          <NoRows>{emptily(revoked, "agent or service token")}</NoRows>
        ) : (
          <List>
            {issued.map((token) => (
              <Row key={token.id} token={token} catalogue={catalogue} onChanged={again} showKind />
            ))}
          </List>
        )}
      </Section>

      <Section
        title="Sessions"
        what="One for every sign-in, this browser's among them. They carry no name, because nobody gives one to a login — what tells them apart is when they appeared and where."
      >
        {tokens.at === "asking" ? (
          <Rows count={2} />
        ) : sessions.length === 0 ? (
          <NoRows>{emptily(revoked, "session")}</NoRows>
        ) : (
          <List>
            {sessions.map((token) => (
              <Row key={token.id} token={token} catalogue={catalogue} onChanged={again} />
            ))}
          </List>
        )}
      </Section>
    </div>
  );
}

/**
 * A dialog that can be taller than the screen, in three classes.
 *
 * The obvious spelling — `max-h` and `overflow-y-auto` on the popup itself —
 * puts the footer inside the scroll container, and the footer is where the
 * button a person came to press is. On a short window the last thing they see
 * is half a button under the fold, with nothing saying there is more. So the
 * popup is a fixed frame instead: one row that may shrink, holding a form or a
 * panel that is itself three rows — a header that stays, a middle that scrolls,
 * and a footer that stays. What grows is the middle, which is the only part
 * that has anything to grow.
 */
const tall = "max-h-[85svh] grid-rows-[minmax(0,1fr)] sm:max-w-lg";

/** The frame inside it: header, the part that scrolls, footer. */
const framed = "grid min-h-0 grid-rows-[auto_minmax(0,1fr)_auto] gap-4";

/** And the part that scrolls. */
const scrolls = "grid min-h-0 gap-4 overflow-y-auto";

/**
 * Whether the screen shows what no longer authenticates.
 *
 * A revoked token is not deleted and never will be: the identity it names is
 * still the author of everything that token ever wrote, and taking the row away
 * would quietly rewrite that record. Which is a reason to keep it, and no reason
 * at all to keep it in the way — a credential retired months ago is not what
 * anybody opening this screen came to see, and on an instance that has been
 * running a while it is most of what they would be shown.
 *
 * So both lists show what still works, and the rest is one switch away. It is
 * one switch and not two because it is one question asked of the instance:
 * `revoked=true` widens the answer both lists are read out of.
 */
function ShowRevoked({ shown, onChange }: { shown: boolean; onChange: (shown: boolean) => void }) {
  return (
    <label className="flex items-center gap-2 text-xs text-muted-foreground">
      <input
        type="checkbox"
        name="revoked"
        className="size-4 accent-primary"
        checked={shown}
        onChange={(event) => onChange(event.target.checked)}
      />
      Show revoked
    </label>
  );
}

/**
 * The two ways a list here can have no rows, which are not the same sentence.
 * "No session on record." to somebody whose sessions are all revoked is a lie
 * they have no way to see through.
 */
function emptily(revoked: boolean, what: string) {
  return revoked
    ? `No ${what} on record, revoked ones included.`
    : `No ${what} that works. Revoked ones are hidden.`;
}

/** A titled list with its own sentence, and whatever acts on it. */
function Section({
  title,
  what,
  action,
  children,
}: {
  title: string;
  what: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    // Named, so that the two lists are two landmarks a screen reader can move
    // between rather than one undifferentiated run of rows.
    <section aria-label={title} className="grid gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-sm font-semibold">{title}</h2>
          <p className="max-w-prose text-xs text-muted-foreground">{what}</p>
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}

function List({ children }: { children: ReactNode }) {
  return <ul className="divide-y rounded-lg border">{children}</ul>;
}

function NoRows({ children }: { children: ReactNode }) {
  return (
    <p className="rounded-lg border border-dashed px-3 py-6 text-center text-xs text-muted-foreground">
      {children}
    </p>
  );
}

/** What a token's standing is, in the word the row shows. */
function standingOf(token: Token): "in use" | "expired" | "revoked" {
  if (token.revokedAt !== null) {
    return "revoked";
  }

  if (token.expiresAt !== null && new Date(token.expiresAt) <= new Date()) {
    return "expired";
  }

  return "in use";
}

/**
 * What is still in use first, in both lists. What is revoked or expired stays
 * below it rather than disappearing: a revocation list that hides revocations is
 * not one, and an expired session is how somebody notices a device they forgot.
 */
function inUseFirst(a: Token, b: Token): number {
  return Number(standingOf(a) !== "in use") - Number(standingOf(b) !== "in use");
}

function Row({
  token,
  catalogue,
  onChanged,
  showKind = false,
}: {
  token: Token;
  catalogue: Project[];
  onChanged: () => void;
  /**
   * Only where it distinguishes something. In the sessions list every row is a
   * session, and a chip repeating the heading on each of them is noise.
   */
  showKind?: boolean;
}) {
  const { me } = useSession();

  const standing = standingOf(token);

  const mine = token.id === me.tokenId;

  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2.5">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline gap-x-2">
          <span className="text-sm font-medium">
            {token.name ?? (token.kind === "session" ? "A signed-in session" : "unnamed")}
          </span>
          {showKind && (
            <span className="rounded-sm border px-1 text-[11px] text-muted-foreground">
              {token.kind}
            </span>
          )}
          {/* Standing is a word: revoked says revoked. */}
          <span
            className={
              standing === "in use"
                ? "text-xs text-muted-foreground"
                : "text-xs text-destructive"
            }
          >
            {standing}
          </span>
          {mine && <span className="text-xs text-muted-foreground">· this browser</span>}
        </div>
        <p className="text-xs text-muted-foreground">
          <span className="font-mono">{token.scopes.join(" ")}</span>
          {" · "}
          {token.reachesTheWholeOrganization
            ? "the whole organization"
            : token.bindings.map((binding) => named(catalogue, binding)).join(", ")}
          <span title={when(token.createdAt)}> · created {around(token.createdAt)}</span>
          {token.expiresAt !== null && standing === "in use" && (
            <span title={when(token.expiresAt)}> · runs out {around(token.expiresAt)}</span>
          )}
        </p>
      </div>

      {/*
        Changing is offered on a token that still works and is not a session. A
        session has no name and its reach is a person's, so there is nothing here
        to arrange; a revoked or expired one authenticates nothing, and widening
        what a dead credential may do would be a control that does nothing.
      */}
      {standing === "in use" && token.kind !== "session" && (
        <>
          <ChangeToken token={token} catalogue={catalogue} onChanged={onChanged} />
          {/*
            And the one thing changing cannot touch. It is beside Change rather
            than behind Revoke because it is what somebody reaches for instead
            of revoking: the value they were worried about stops working either
            way, and this way the credential goes on existing.
          */}
          <RotateToken token={token} onRotated={onChanged} />
        </>
      )}

      {token.revokedAt === null && (
        <ActionDialog
          trigger={
            <Button variant="ghost" size="sm">
              Revoke
            </Button>
          }
          title={`Revoke ${token.name ?? "this session"}?`}
          description={
            mine
              ? "This is the session this browser is signed in with. Revoking it signs you out here, immediately."
              : "Whatever is holding it stops working at once — an agent mid-task, a deployment, a person's other browser. The row stays revoked rather than deleted, so everything it ever changed keeps an author."
          }
          confirmLabel="Revoke"
          onConfirm={async () => {
            await answered(
              api.DELETE("/api/v1/tokens/{id}", { params: { path: { id: token.id } } }),
            );

            onChanged();
          }}
        />
      )}

      {/*
        And removing the row for good, which only a revoked one is offered. That
        order is the whole rule: a revocation stands until somebody deliberately
        takes it off the list, so nothing disappears from here while it still
        works, and a list of revocations is only honest for as long as it keeps
        them.
      */}
      {token.revokedAt !== null && (
        <ActionDialog
          trigger={
            <Button variant="ghost" size="sm">
              Delete
            </Button>
          }
          title={`Delete ${token.name ?? "this revoked session"} for good?`}
          description={
            <>
              <span className="block">
                It is revoked already, so nothing stops working. What goes is the row: it leaves
                this list, and no listing here mentions it again.
              </span>
              <span className="mt-2 block font-medium text-foreground">
                The change log keeps everything it did. Entries name the identity that made them
                rather than pointing at this row, which is exactly so that the row can go without
                taking the history with it.
              </span>
            </>
          }
          confirmLabel="Delete for good"
          onConfirm={async () => {
            await answered(
              api.POST("/api/v1/tokens/{id}/purge", { params: { path: { id: token.id } } }),
            );

            onChanged();
          }}
        />
      )}
    </li>
  );
}

/** A binding, in the names a person reads rather than the ids it is made of. */
function named(catalogue: Project[], binding: { projectId: string; environmentId: string | null }) {
  const project = catalogue.find((one) => one.id === binding.projectId);

  if (project === undefined) {
    return "a project of this organization";
  }

  if (binding.environmentId === null) {
    return `${project.name} (every environment)`;
  }

  const environment = project.environments.find((one) => one.id === binding.environmentId);

  return `${project.name}/${environment?.name ?? "an environment"}`;
}


function NewToken({ catalogue, onCreated }: { catalogue: Project[]; onCreated: () => void }) {
  const empty: Draft = { name: "", scopes: [...scopes], reach: "organization", picked: [] };

  const [open, setOpen] = useState(false);
  const [kind, setKind] = useState<"agent" | "service">("agent");
  const [draft, setDraft] = useState<Draft>(empty);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [value, setValue] = useState<string>();

  function change(next: boolean) {
    setOpen(next);

    if (!next) {
      setKind("agent");
      setDraft(empty);
      setRefusal(undefined);
      setValue(undefined);
    }
  }

  function changeKind(next: "agent" | "service") {
    setKind(next);
    // The default of that kind, as the instance would apply it: everything for
    // an agent, names and read for a service.
    setDraft((current) => ({
      ...current,
      scopes: next === "agent" ? [...scopes] : ["names", "read"],
    }));
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST("/api/v1/tokens", {
        body: {
          kind,
          name: draft.name,
          scopes: draft.scopes,
          bindings: bindingsOf(catalogue, draft),
        },
      });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      setValue(data.value);
      onCreated();
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Button size="sm" onClick={() => change(true)}>
        <PlusIcon />
        New token
      </Button>

      <Dialog open={open} onOpenChange={change}>
        <DialogContent className={tall}>
          {value === undefined ? (
            <form className={framed} onSubmit={(event) => void submit(event)}>
              <DialogHeader>
                <DialogTitle>A new token</DialogTitle>
                <DialogDescription>
                  A token is a credential an agent or a service acts under, so that the change log
                  can say what kind of thing acted. Its value appears once, on the next screen.
                </DialogDescription>
              </DialogHeader>

              <div className={scrolls}>
                <fieldset className="grid gap-2">
                  <legend className="text-sm font-medium">Kind</legend>
                  <Choice
                    name="kind"
                    checked={kind === "agent"}
                    onChange={() => changeKind("agent")}
                    label="Agent"
                    what="For an agent working in somebody's terminal. It acts under its own name so the log does not say a person's for everything it did."
                  />
                  <Choice
                    name="kind"
                    checked={kind === "service"}
                    onChange={() => changeKind("service")}
                    label="Service"
                    what="For an application or a pipeline. Narrower by default: the names of keys, and their values."
                  />
                </fieldset>

                <TokenFields
                  catalogue={catalogue}
                  draft={draft}
                  change={(part) => setDraft((current) => ({ ...current, ...part }))}
                  hint="What a revocation list will call it, months from now."
                />

                {refusal !== undefined && <Refusal>{refusal}</Refusal>}
              </div>

              <DialogFooter>
                <Button variant="outline" disabled={busy} onClick={() => change(false)}>
                  Cancel
                </Button>
                <Button
                  type="submit"
                  disabled={busy || draft.name.trim() === "" || draft.scopes.length === 0}
                >
                  {busy ? "Creating…" : "Create the token"}
                </Button>
              </DialogFooter>
            </form>
          ) : (
            <TheValueOnce
              title={`The value of ${draft.name}`}
              value={value}
              onDone={() => change(false)}
            >
              <strong>This is the once.</strong> The instance stores a hash of it and can never
              show it again. Copy it into wherever it is meant to live — an agent's environment as{" "}
              <span className="font-mono">VAULTAFFE_TOKEN</span>, a deployment's secret store — and
              if it is lost, revoke this one and create another.
            </TheValueOnce>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
}

/**
 * Changing one that already exists: its name, what it may do, and how far it
 * reaches.
 *
 * **The value is untouched**, and the dialog says so, because that is the whole
 * point of the act. Nothing holding this token has to be told anything: the
 * agent keeps running, the pipeline keeps deploying, and what changed is what
 * the instance lets the same string through for. The alternative this replaces —
 * issue a second token, go round every machine that holds the first, revoke it —
 * is how a credential ends up copied into more places than anybody can list.
 *
 * The kind is not here. A service token that became an agent token would be a
 * different identity in the change log with the same history behind it, and
 * nothing about "attribution, not restriction" survives that
 * ([§6.4](../../../../Specification.md#64-permissions-in-the-mvp)). Neither is
 * the expiry: what runs out is what a person agreed to when they issued it.
 */
function ChangeToken({
  token,
  catalogue,
  onChanged,
}: {
  token: Token;
  catalogue: Project[];
  onChanged: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<Draft>(() => draftOf(token));
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();

  function change(next: boolean) {
    setOpen(next);

    // Opening reads the token again rather than trusting what the last cancelled
    // edit left behind: the list is re-asked after every act on this screen, and
    // a dialog that opened on a stale draft would save yesterday's answer.
    if (next) {
      setDraft(draftOf(token));
      setRefusal(undefined);
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.PATCH("/api/v1/tokens/{id}", {
        params: { path: { id: token.id } },
        body: {
          name: draft.name,
          scopes: draft.scopes,
          bindings: bindingsOf(catalogue, draft),
        },
      });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      onChanged();
      setOpen(false);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Button variant="ghost" size="sm" onClick={() => change(true)}>
        Change
      </Button>

      <Dialog open={open} onOpenChange={change}>
        <DialogContent className={tall}>
          <form className={framed} onSubmit={(event) => void submit(event)}>
            <DialogHeader>
              <DialogTitle>{token.name ?? "This token"}</DialogTitle>
              <DialogDescription>
                Its value does not change and is not shown again. Whatever is holding this token
                keeps working; what changes is what this instance lets it do.
              </DialogDescription>
            </DialogHeader>

            <div className={scrolls}>
              <TokenFields
                catalogue={catalogue}
                draft={draft}
                change={(part) => setDraft((current) => ({ ...current, ...part }))}
                hint="What a revocation list will call it, months from now. Renaming changes nothing else."
              />

              {refusal !== undefined && <Refusal>{refusal}</Refusal>}
            </div>

            <DialogFooter>
              <Button variant="outline" disabled={busy} onClick={() => change(false)}>
                Cancel
              </Button>
              <Button
                type="submit"
                disabled={busy || draft.name.trim() === "" || draft.scopes.length === 0}
              >
                {busy ? "Saving…" : "Save the change"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}

/**
 * A value, in the one moment it exists.
 *
 * Two dialogs end here — a token that was just created and one that was just
 * rotated — and they end the same way on purpose: the instance keeps a hash,
 * this screen is the only place the string will ever be, and a person has to be
 * told that in the same breath they are handed it.
 */
function TheValueOnce({
  title,
  value,
  onDone,
  children,
}: {
  title: string;
  value: string;
  onDone: () => void;
  /** What this particular value is, and what to do with it. */
  children: ReactNode;
}) {
  return (
    <div className={framed}>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{children}</DialogDescription>
      </DialogHeader>

      <div className={scrolls}>
        <Copyable value={value} label="token value" />
        <p className="text-xs text-muted-foreground">
          A token in a process environment is plaintext on that machine. That is inside this
          product's threat model and worth knowing when choosing where to put it.
        </p>
      </div>

      <DialogFooter>
        <Button onClick={onDone}>Done</Button>
      </DialogFooter>
    </div>
  );
}

/**
 * Replacing the value of a token that already exists.
 *
 * **The one thing changing cannot touch**, and the reason this is a third act
 * on the row rather than a field in the second: a name, a scope set and a reach
 * are arranged without anybody being visited, and none of that helps when what
 * went wrong is the value itself. Before this existed, a leaked value meant
 * revoking the token and creating another — a second name in the change log for
 * the same worker, on two sides of an incident.
 *
 * **It asks first, and the question says what it costs** rather than asserting
 * that it is safe. The value in an agent's environment or a deployment's secret
 * store is dead the moment this is confirmed, with no overlap: a rotation is
 * usually the answer to a value having gone somewhere it should not be, and a
 * grace period is exactly as useful to whoever has it as to the run still
 * holding the good one.
 *
 * What survives is everything else. The name, the scopes and the reach come
 * across untouched, and so does the length of the expiry — a token issued to run
 * for thirty days gets thirty days again. The row it had stays, revoked, which
 * is where the day the value changed is read afterwards.
 */
function RotateToken({ token, onRotated }: { token: Token; onRotated: () => void }) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [value, setValue] = useState<string>();

  function change(next: boolean) {
    // Closing on the value is the one moment this dialog is not reopenable:
    // what it was showing is gone, and the row behind it has already been
    // re-read.
    setOpen(next);

    if (!next) {
      setRefusal(undefined);
      setValue(undefined);
    }
  }

  async function rotate() {
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST("/api/v1/tokens/{id}/rotate", {
        params: { path: { id: token.id } },
      });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      setValue(data.value);
      onRotated();
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Button variant="ghost" size="sm" onClick={() => change(true)}>
        Rotate
      </Button>

      <Dialog open={open} onOpenChange={change}>
        <DialogContent className={tall}>
          {value === undefined ? (
            <div className={framed}>
              <DialogHeader>
                <DialogTitle>Rotate {token.name ?? "this token"}?</DialogTitle>
                <DialogDescription>
                  It keeps its name, its scopes and its reach, and is given a new value on the next
                  screen.
                </DialogDescription>
              </DialogHeader>

              <div className={scrolls}>
                <p className="text-sm">
                  <strong>The value it has now stops working immediately.</strong> Whatever is
                  holding it — an agent mid-task, a deployment, a pipeline — fails on its next
                  request until it is given the new one. There is no overlap, on purpose: a
                  rotation is usually the answer to a value having gone somewhere it should not be.
                </p>
                <p className="text-xs text-muted-foreground">
                  The row it has now stays, revoked rather than deleted, so that the day the value
                  in circulation changed is something this instance can still tell you. Show
                  revoked to read it.
                </p>

                {refusal !== undefined && <Refusal>{refusal}</Refusal>}
              </div>

              <DialogFooter>
                <Button variant="outline" disabled={busy} onClick={() => change(false)}>
                  Cancel
                </Button>
                <Button disabled={busy} onClick={() => void rotate()}>
                  {busy ? "Rotating…" : "Rotate it"}
                </Button>
              </DialogFooter>
            </div>
          ) : (
            <TheValueOnce
              title={`The new value of ${token.name ?? "this token"}`}
              value={value}
              onDone={() => change(false)}
            >
              <strong>This is the once.</strong> The one it replaces is revoked and authenticates
              nothing from here on. Put this where that one was — an agent's environment as{" "}
              <span className="font-mono">VAULTAFFE_TOKEN</span>, a deployment's secret store — and
              whatever is still carrying the old value will start working again.
            </TheValueOnce>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
}
