import { PlusIcon } from "lucide-react";
import { useState } from "react";
import { Link, useParams } from "react-router";
import { answered, api, type Secret } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import { ActionDialog } from "@/shared/ActionDialog";
import { Refusal } from "@/shared/Form";
import { around, when } from "@/shared/moments";
import { PageHeader } from "@/shared/PageHeader";
import { Rows } from "@/shared/Rows";
import { projectPath, secretPath } from "@/shell/views";
import { Deleted } from "./Deleted";
import { ExportDialog, ImportDialog } from "./EnvironmentFile";
import { WriteDialog } from "./WriteDialog";

/**
 * `/projects/:project/:environment` — **the main screen**: the keys of this
 * environment, their status, and when each was last written
 * (`docs/human-interface.md`).
 *
 * **There is no value column, and there is no width at which one appears.** The
 * listing endpoint answers names and status and has no values in it at all
 * ([`api.md`](../../../../docs/api.md)), so this screen could not show one if it
 * wanted to — which is what makes the masking rule true rather than decorative.
 * Revealing is an act on one key, on that key's own screen.
 */
export function Environment() {
  const { project = "", environment = "" } = useParams();
  const [deleted, setDeleted] = useState(false);

  const [secrets, again] = useAsk<Secret[]>(
    `secrets:${project}/${environment}:${deleted}`,
    () =>
      api.GET("/api/v1/projects/{project}/environments/{environment}/secrets", {
        params: { path: { project, environment }, query: { deleted } },
      }),
  );

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
            <span className="font-mono">{environment}</span>
          </span>
        }
      >
        <Deleted showing={deleted} onShowing={setDeleted} />
        <ImportDialog project={project} environment={environment} onImported={again} />
        <ExportDialog project={project} environment={environment} />
        <WriteDialog
          project={project}
          environment={environment}
          trigger={
            <Button size="sm">
              <PlusIcon />
              New key
            </Button>
          }
          onWritten={again}
        />
      </PageHeader>

      <div className="flex flex-1 flex-col gap-3 p-4">
        {secrets.at === "asking" && <Rows count={5} />}
        {secrets.at === "refused" && <Refusal>{secrets.why}</Refusal>}
        {secrets.at === "answered" &&
          (secrets.data.length === 0 ? (
            <Nothing
              deleted={deleted}
              project={project}
              environment={environment}
              onChanged={again}
            />
          ) : (
            <ul className="divide-y rounded-lg border">
              {secrets.data.map((secret) => (
                <Row
                  key={secret.id}
                  project={project}
                  environment={environment}
                  secret={secret}
                  onChanged={again}
                />
              ))}
            </ul>
          ))}
      </div>
    </>
  );
}

/**
 * A key: its name, its status, and when its value was last written. Two lines on
 * a narrow screen, and never a horizontal scroll.
 *
 * **empty** is not an error state and is not styled as one. It means a person
 * still has to do something — it is what stops `run`, and it is the state an
 * agent deliberately creates when it prepares work for a human
 * ([Specification §6.2](../../../../Specification.md#62-cli)).
 */
function Row({
  project,
  environment,
  secret,
  onChanged,
}: {
  project: string;
  environment: string;
  secret: Secret;
  onChanged: () => void;
}) {
  const gone = secret.deletedAt !== null;

  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2.5">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline gap-x-2">
          {gone ? (
            <span className="font-mono text-sm break-all">{secret.name}</span>
          ) : (
            <Link
              className="font-mono text-sm font-medium break-all hover:underline"
              to={secretPath(project, environment, secret.name)}
            >
              {secret.name}
            </Link>
          )}
          {/* A word and a shape, never a colour alone. */}
          <span
            className={
              secret.status === "set"
                ? "rounded-sm border px-1 text-[11px] text-muted-foreground"
                : "rounded-sm border border-dashed px-1 text-[11px] text-muted-foreground"
            }
          >
            {secret.status}
          </span>
        </div>
        <p className="text-xs text-muted-foreground">
          {secret.status === "empty" ? (
            "waiting for a person to fill it"
          ) : secret.valueWrittenAt != null ? (
            <span title={when(secret.valueWrittenAt)}>written {around(secret.valueWrittenAt)}</span>
          ) : (
            "never written"
          )}
          {gone && (
            <span className="text-destructive" title={when(secret.deletedAt)}>
              {" · "}deleted {around(secret.deletedAt)}
            </span>
          )}
        </p>
      </div>

      {gone ? (
        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            void answered(
              api.POST(
                "/api/v1/projects/{project}/environments/{environment}/secrets/{name}/restore",
                { params: { path: { project, environment, name: secret.name } } },
              ),
            ).then(onChanged);
          }}
        >
          Restore
        </Button>
      ) : (
        <ActionDialog
          trigger={
            <Button variant="ghost" size="sm">
              Delete
            </Button>
          }
          title={`Delete ${secret.name}?`}
          description="It leaves this environment at once and anything reading it stops finding one. It can be restored for 72 hours, with the value it holds now."
          confirmLabel="Delete"
          onConfirm={async () => {
            await answered(
              api.DELETE("/api/v1/projects/{project}/environments/{environment}/secrets/{name}", {
                params: { path: { project, environment, name: secret.name } },
              }),
            );

            onChanged();
          }}
        />
      )}
    </li>
  );
}

/**
 * An environment with no keys says what a first key is for, and offers import
 * beside creating one — the guiding case for an empty environment is a team
 * arriving from `.env` files
 * ([Specification §11](../../../../Specification.md#11-success-criteria-for-the-mvp)).
 */
function Nothing({
  deleted,
  project,
  environment,
  onChanged,
}: {
  deleted: boolean;
  project: string;
  environment: string;
  onChanged: () => void;
}) {
  if (deleted) {
    return (
      <p className="rounded-lg border border-dashed px-3 py-10 text-center text-sm text-muted-foreground">
        Nothing in this environment has been deleted in the last 72 hours.
      </p>
    );
  }

  return (
    <div className="grid justify-items-center gap-3 rounded-lg border border-dashed px-3 py-10 text-center">
      <p className="text-sm font-medium">No keys in this environment yet.</p>
      <p className="max-w-prose text-sm text-muted-foreground">
        A key is one name and one value — the connection string an application reads, the token a
        service needs. If those live in a <code className="font-mono">.env</code> file today, bring
        the file over and delete it afterwards.
      </p>
      <div className="flex flex-wrap justify-center gap-2">
        <ImportDialog project={project} environment={environment} onImported={onChanged} />
        <WriteDialog
          project={project}
          environment={environment}
          trigger={<Button size="sm">Create the first key</Button>}
          onWritten={onChanged}
        />
      </div>
    </div>
  );
}
