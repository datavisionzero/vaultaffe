import { EyeIcon, EyeOffIcon } from "lucide-react";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { answered, api, describe, type Secret as SecretRow } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ActionDialog } from "@/shared/ActionDialog";
import { Copyable } from "@/shared/Copyable";
import { Refusal } from "@/shared/Form";
import { around, when } from "@/shared/moments";
import { PageHeader } from "@/shared/PageHeader";
import { environmentPath, projectPath } from "@/shell/views";
import { History } from "./History";
import { WriteDialog } from "./WriteDialog";

/**
 * `/projects/:project/:environment/:KEY` — one key: the masked value with its
 * reveal, and the acts on it (`docs/human-interface.md`).
 *
 * **The screen does not hold the value.** Opening it reads names and status;
 * revealing is a second request for one key, which is why the rule is true
 * rather than decorative — a screenshot of this page before a reveal cannot leak
 * what was never sent. Leaving the screen, reloading it or coming back with the
 * back button all start masked again, because nothing about a reveal is kept
 * anywhere: not in `sessionStorage`, not in the URL, not in this component after
 * it unmounts.
 */
export function Secret() {
  const { project = "", environment = "", name = "" } = useParams();
  const navigate = useNavigate();

  const [asked, again] = useAsk<SecretRow[]>(`secrets:${project}/${environment}`, () =>
    api.GET("/api/v1/projects/{project}/environments/{environment}/secrets", {
      params: { path: { project, environment }, query: { deleted: false } },
    }),
  );

  const secret =
    asked.at === "answered" ? asked.data.find((one) => one.name === name) : undefined;

  return (
    <>
      <PageHeader
        title={
          <span className="flex flex-wrap items-baseline gap-1">
            <Link className="font-mono text-muted-foreground hover:underline" to={projectPath(project)}>
              {project}
            </Link>
            <span aria-hidden className="text-muted-foreground">
              /
            </span>
            <Link
              className="font-mono text-muted-foreground hover:underline"
              to={environmentPath(project, environment)}
            >
              {environment}
            </Link>
            <span aria-hidden className="text-muted-foreground">
              /
            </span>
            <span className="font-mono break-all">{name}</span>
          </span>
        }
      >
        {secret !== undefined && (
          <>
            <WriteDialog
              project={project}
              environment={environment}
              name={name}
              trigger={<Button size="sm">Write a value</Button>}
              onWritten={again}
            />
            <ActionDialog
              trigger={
                <Button variant="ghost" size="sm">
                  Delete
                </Button>
              }
              title={`Delete ${name}?`}
              description="It leaves this environment at once and anything reading it stops finding one. It can be restored for 72 hours, with the value it holds now."
              confirmLabel="Delete"
              onConfirm={async () => {
                await answered(
                  api.DELETE(
                    "/api/v1/projects/{project}/environments/{environment}/secrets/{name}",
                    { params: { path: { project, environment, name } } },
                  ),
                );

                void navigate(environmentPath(project, environment), { replace: true });
              }}
            />
          </>
        )}
      </PageHeader>

      <div className="flex flex-1 flex-col gap-6 p-4">
        {asked.at === "asking" && <Skeleton className="h-24 w-full max-w-lg" />}
        {asked.at === "refused" && <Refusal>{asked.why}</Refusal>}
        {asked.at === "answered" &&
          (secret === undefined ? (
            <p className="text-sm text-muted-foreground">
              There is no key called <span className="font-mono">{name}</span> in{" "}
              <span className="font-mono">
                {project}/{environment}
              </span>
              . It may have been deleted — the environment screen shows what is still recoverable.
            </p>
          ) : (
            <>
              <Value
                project={project}
                environment={environment}
                secret={secret}
                onFilled={again}
              />
              <History
                project={project}
                environment={environment}
                secret={secret}
                onChanged={again}
              />
            </>
          ))}
      </div>
    </>
  );
}

/**
 * The value block: what the key is in, and the one act that fetches it.
 *
 * A masked value is **not a field full of bullet characters** a screen reader
 * spells out. It is a control that says what it is, and the revealed value is a
 * region a reader can be taken to and that announces itself
 * (`docs/human-interface.md`, the accessibility half of the masking rule).
 * Nothing here depends on hovering.
 */
function Value({
  project,
  environment,
  secret,
  onFilled,
}: {
  project: string;
  environment: string;
  secret: SecretRow;
  onFilled: () => void;
}) {
  const [read, setRead] = useState<{ of: string; value: string }>();
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();

  // What was revealed belongs to the value that was revealed. A key that was
  // rewritten, or a reader who walked to another one, therefore starts masked
  // again — read here rather than cleared in an effect, so there is no render in
  // between showing what is no longer true.
  const of = `${secret.id}:${secret.valueWrittenAt ?? ""}`;
  const revealed = read?.of === of ? read.value : undefined;

  async function fetchValue(): Promise<string | undefined> {
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.GET(
        "/api/v1/projects/{project}/environments/{environment}/secrets/{name}",
        { params: { path: { project, environment, name: secret.name } } },
      );

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return undefined;
      }

      return data.value ?? undefined;
    } finally {
      setBusy(false);
    }
  }

  if (secret.status === "empty") {
    return (
      <section className="grid max-w-lg gap-3 rounded-lg border border-dashed p-4">
        <div>
          <h2 className="text-sm font-medium">This key is empty</h2>
          <p className="text-sm text-muted-foreground">
            It exists and holds nothing, which is a state somebody meant: it stops an application
            from starting with a value nobody chose, and it is how an agent asks a person for one.
          </p>
        </div>
        <div>
          <WriteDialog
            project={project}
            environment={environment}
            name={secret.name}
            trigger={<Button size="sm">Fill it</Button>}
            onWritten={onFilled}
          />
        </div>
      </section>
    );
  }

  return (
    <section className="grid max-w-lg gap-3 rounded-lg border p-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-sm font-medium">Value</h2>
        <span className="text-xs text-muted-foreground" title={when(secret.valueWrittenAt)}>
          written {around(secret.valueWrittenAt)}
        </span>
      </div>

      {revealed === undefined ? (
        <p className="text-sm text-muted-foreground">Value, hidden.</p>
      ) : (
        <output
          aria-live="polite"
          className="block rounded-md border bg-muted/50 px-2 py-1.5 font-mono text-xs break-all"
        >
          {revealed}
        </output>
      )}

      {refusal !== undefined && <Refusal>{refusal}</Refusal>}

      <div className="flex flex-wrap gap-2">
        {revealed === undefined ? (
          <Button
            variant="outline"
            size="sm"
            disabled={busy}
            onClick={() => {
              void fetchValue().then((value) => {
                if (value !== undefined) {
                  setRead({ of, value });
                }
              });
            }}
          >
            <EyeIcon />
            {busy ? "Reading…" : "Reveal it"}
          </Button>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setRead(undefined)}>
            <EyeOffIcon />
            Hide it again
          </Button>
        )}

        {/* Copying is revealing: it fetches the value exactly as the eye does,
            and puts it on the clipboard rather than on the screen. */}
        {revealed === undefined ? (
          <Button
            variant="outline"
            size="sm"
            disabled={busy}
            onClick={() => {
              void fetchValue().then((value) => {
                if (value !== undefined) {
                  void navigator.clipboard.writeText(value);
                }
              });
            }}
          >
            Read it and copy
          </Button>
        ) : (
          <Copyable value={revealed} label="value" hidden />
        )}
      </div>
    </section>
  );
}
