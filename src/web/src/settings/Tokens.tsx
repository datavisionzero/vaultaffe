import { PlusIcon } from "lucide-react";
import { useMemo, useState, type FormEvent } from "react";
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
import { Field, Refusal } from "@/shared/Form";
import { around, when } from "@/shared/moments";
import { Rows } from "@/shared/Rows";

/** The four scopes, in the order the specification declares them. */
const scopes = ["names", "read", "write", "delete"] as const;

type Scope = (typeof scopes)[number];

/**
 * What each scope is, said where a person is choosing one rather than in a
 * document they would have to go and find.
 */
const whatScopeIs: Record<Scope, string> = {
  names: "See which keys exist and which are still empty. Never a value.",
  read: "Read one value at a time.",
  write: "Write values, create keys and placeholders, roll one back.",
  delete: "Delete a secret, an environment or a project — recoverably — and restore one.",
};

/** The environments a token bound to nothing but production would not reach. */
const production = ["prod", "production"];

/**
 * `/settings/tokens` — the organization's tokens with kind, name, scopes,
 * binding and standing; creating one; and the value of a new one, once
 * (`docs/human-interface.md`).
 *
 * **Creating and revoking are a person's** and the instance says so: a token is
 * itself a secret, and one an agent created through the CLI would be printed to
 * its own stdout and into its context
 * ([Specification §6.1](../../../../Specification.md#61-web-ui)). Listing is not
 * on that list — a revocation list an agent cannot read is not one.
 *
 * The default for an agent token is **the whole organization with every scope**,
 * because the point is attribution and not restriction, and the first narrowing
 * offered is keeping it out of production
 * ([§6.4](../../../../Specification.md#64-permissions-in-the-mvp)).
 */
export function Tokens() {
  const [tokens, again] = useAsk<Token[]>("tokens", () => api.GET("/api/v1/tokens"));
  const [projects] = useAsk<Project[]>("tokens:projects", () => api.GET("/api/v1/projects"));

  const catalogue = projects.at === "answered" ? projects.data : [];

  return (
    <div className="grid gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-sm font-semibold">Tokens</h2>
          <p className="text-xs text-muted-foreground">
            Every token of this organization, newest first, revoked ones and sessions among them.
            No listing anywhere carries a value.
          </p>
        </div>
        <NewToken catalogue={catalogue} onCreated={again} />
      </div>

      {tokens.at === "asking" && <Rows count={4} />}
      {tokens.at === "refused" && <Refusal>{tokens.why}</Refusal>}
      {tokens.at === "answered" && (
        <ul className="divide-y rounded-lg border">
          {tokens.data.map((token) => (
            <Row key={token.id} token={token} catalogue={catalogue} onChanged={again} />
          ))}
        </ul>
      )}
    </div>
  );
}

function Row({
  token,
  catalogue,
  onChanged,
}: {
  token: Token;
  catalogue: Project[];
  onChanged: () => void;
}) {
  const { me } = useSession();

  const standing =
    token.revokedAt !== null
      ? "revoked"
      : token.expiresAt !== null && new Date(token.expiresAt) <= new Date()
        ? "expired"
        : "in use";

  const mine = token.id === me.tokenId;

  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2.5">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline gap-x-2">
          <span className="text-sm font-medium">
            {token.name ?? (token.kind === "session" ? "A signed-in session" : "unnamed")}
          </span>
          <span className="rounded-sm border px-1 text-[11px] text-muted-foreground">
            {token.kind}
          </span>
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

type Reach = "organization" | "not-production" | "picked";

function NewToken({ catalogue, onCreated }: { catalogue: Project[]; onCreated: () => void }) {
  const [open, setOpen] = useState(false);
  const [kind, setKind] = useState<"agent" | "service">("agent");
  const [name, setName] = useState("");
  const [chosen, setChosen] = useState<Scope[]>([...scopes]);
  const [reach, setReach] = useState<Reach>("organization");
  const [picked, setPicked] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [value, setValue] = useState<string>();

  // What "everything except production" actually binds to: every environment
  // that is not called prod. Shown rather than implied, because an inclusive
  // binding made out of an exclusion is worth seeing before it is issued.
  const away = useMemo(
    () =>
      catalogue.flatMap((project) =>
        project.environments
          .filter((environment) => !production.includes(environment.name))
          .map((environment) => environment.id),
      ),
    [catalogue],
  );

  function change(next: boolean) {
    setOpen(next);

    if (!next) {
      setKind("agent");
      setName("");
      setChosen([...scopes]);
      setReach("organization");
      setPicked([]);
      setRefusal(undefined);
      setValue(undefined);
    }
  }

  function changeKind(next: "agent" | "service") {
    setKind(next);
    // The default of that kind, as the instance would apply it: everything for
    // an agent, names and read for a service.
    setChosen(next === "agent" ? [...scopes] : ["names", "read"]);
  }

  function bindings(): { projectId: string; environmentId: string | null }[] {
    const ids = reach === "not-production" ? away : reach === "picked" ? picked : [];

    return catalogue.flatMap((project) =>
      project.environments
        .filter((environment) => ids.includes(environment.id))
        .map((environment) => ({ projectId: project.id, environmentId: environment.id })),
    );
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST("/api/v1/tokens", {
        body: { kind, name, scopes: chosen, bindings: bindings() },
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
        <DialogContent className="max-h-[85svh] overflow-y-auto sm:max-w-lg">
          {value === undefined ? (
            <form className="grid gap-4" onSubmit={(event) => void submit(event)}>
              <DialogHeader>
                <DialogTitle>A new token</DialogTitle>
                <DialogDescription>
                  A token is a credential an agent or a service acts under, so that the change log
                  can say what kind of thing acted. Its value appears once, on the next screen.
                </DialogDescription>
              </DialogHeader>

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

              <Field
                label="Name"
                required
                maxLength={100}
                value={name}
                onChange={(event) => setName(event.target.value)}
                hint="What a revocation list will call it, months from now."
              />

              <fieldset className="grid gap-2">
                <legend className="text-sm font-medium">Scopes</legend>
                {scopes.map((scope) => (
                  <label key={scope} className="flex items-start gap-2 text-sm">
                    <input
                      type="checkbox"
                      name={`scope-${scope}`}
                      className="mt-0.5 size-4 accent-primary"
                      checked={chosen.includes(scope)}
                      onChange={(event) =>
                        setChosen((current) =>
                          event.target.checked
                            ? [...scopes].filter((one) => current.includes(one) || one === scope)
                            : current.filter((one) => one !== scope),
                        )
                      }
                    />
                    <span>
                      <span className="font-mono">{scope}</span>
                      <span className="block text-xs text-muted-foreground">
                        {whatScopeIs[scope]}
                      </span>
                    </span>
                  </label>
                ))}
              </fieldset>

              <fieldset className="grid gap-2">
                <legend className="text-sm font-medium">What it reaches</legend>
                <Choice
                  name="reach"
                  checked={reach === "organization"}
                  onChange={() => setReach("organization")}
                  label="The whole organization"
                  what="The default for an agent token: the point is attribution, not restriction."
                />
                <Choice
                  name="reach"
                  checked={reach === "not-production"}
                  onChange={() => setReach("not-production")}
                  label="Everything except production"
                  what={
                    away.length === 0
                      ? "There is nothing outside production to bind to yet."
                      : `Binds it to ${away.length} environments — every one not called prod or production.`
                  }
                />
                <Choice
                  name="reach"
                  checked={reach === "picked"}
                  onChange={() => setReach("picked")}
                  label="Only what I pick"
                  what="One project, one environment, or any set of them."
                />

                {reach === "picked" && (
                  <div className="grid gap-2 rounded-lg border p-2">
                    {catalogue.length === 0 && (
                      <p className="text-xs text-muted-foreground">
                        There are no projects to bind to yet.
                      </p>
                    )}
                    {catalogue.map((project) => (
                      <div key={project.id}>
                        <p className="font-mono text-xs text-muted-foreground">{project.name}</p>
                        <div className="flex flex-wrap gap-x-4">
                          {project.environments.map((environment) => (
                            <label
                              key={environment.id}
                              className="flex items-center gap-1.5 text-sm"
                            >
                              <input
                                type="checkbox"
                                name={`environment-${environment.id}`}
                                className="size-3.5 accent-primary"
                                checked={picked.includes(environment.id)}
                                onChange={(event) =>
                                  setPicked((current) =>
                                    event.target.checked
                                      ? [...current, environment.id]
                                      : current.filter((one) => one !== environment.id),
                                  )
                                }
                              />
                              <span className="font-mono text-xs">{environment.name}</span>
                            </label>
                          ))}
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </fieldset>

              {refusal !== undefined && <Refusal>{refusal}</Refusal>}

              <DialogFooter>
                <Button variant="outline" disabled={busy} onClick={() => change(false)}>
                  Cancel
                </Button>
                <Button type="submit" disabled={busy || name.trim() === "" || chosen.length === 0}>
                  {busy ? "Creating…" : "Create the token"}
                </Button>
              </DialogFooter>
            </form>
          ) : (
            <div className="grid gap-4">
              <DialogHeader>
                <DialogTitle>The value of {name}</DialogTitle>
                <DialogDescription>
                  <strong>This is the once.</strong> The instance stores a hash of it and can never
                  show it again. Copy it into wherever it is meant to live — an agent's environment
                  as <span className="font-mono">VAULTAFFE_TOKEN</span>, a deployment's secret
                  store — and if it is lost, revoke this one and create another.
                </DialogDescription>
              </DialogHeader>
              <Copyable value={value} label="token value" />
              <p className="text-xs text-muted-foreground">
                A token in a process environment is plaintext on that machine. That is inside this
                product's threat model and worth knowing when choosing where to put it.
              </p>
              <DialogFooter>
                <Button onClick={() => change(false)}>Done</Button>
              </DialogFooter>
            </div>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
}

/** One of a set of choices, with what it means under it rather than beside it. */
function Choice({
  name,
  checked,
  onChange,
  label,
  what,
}: {
  name: string;
  checked: boolean;
  onChange: () => void;
  label: string;
  what: string;
}) {
  return (
    <label className="flex items-start gap-2 text-sm">
      <input
        type="radio"
        name={name}
        className="mt-0.5 size-4 accent-primary"
        checked={checked}
        onChange={onChange}
      />
      <span>
        {label}
        <span className="block text-xs text-muted-foreground">{what}</span>
      </span>
    </label>
  );
}
