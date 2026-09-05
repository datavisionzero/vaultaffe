import { PlusIcon } from "lucide-react";
import { Link, useNavigate, useParams } from "react-router";
import { answered, api, type Environment, type Project, type Secret } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import { ActionDialog, TextActionDialog } from "@/shared/ActionDialog";
import { Refusal } from "@/shared/Form";
import { around, when } from "@/shared/moments";
import { PageHeader } from "@/shared/PageHeader";
import { PurgeDialog } from "@/shared/Purge";
import { Rows } from "@/shared/Rows";
import { environmentPath, projectPath } from "@/shell/views";
import { useState } from "react";
import { Deleted } from "./Deleted";

/**
 * `/projects/:project` — the project's environments, each with a key count and
 * when a value in it was last written, and the project's own acts
 * (`docs/human-interface.md`).
 *
 * The count and the moment are read from each environment's own listing of
 * **names and status**, which is the endpoint that exists and the one that
 * cannot carry a value. A project has a handful of environments, so a handful of
 * listings is what a count costs; the alternative would be a summary field on
 * the catalogue, and a number is not worth widening the contract for.
 */
export function Project() {
  const { project = "" } = useParams();
  const navigate = useNavigate();
  const [deleted, setDeleted] = useState(false);
  const [asked, again] = useAsk<Project>(`project:${project}`, () =>
    api.GET("/api/v1/projects/{project}", { params: { path: { project } } }),
  );

  return (
    <>
      <PageHeader title={<span className="font-mono">{project}</span>} meta="Project">
        <Deleted showing={deleted} onShowing={setDeleted} />
        {asked.at === "answered" && (
          <>
            <NewEnvironment project={project} onCreated={again} />
            <TextActionDialog
              trigger={
                <Button variant="ghost" size="sm">
                  Rename
                </Button>
              }
              title={`Rename ${project}`}
              description="Every reference to this project is by name — the CLI's binding, a vaultaffe:// reference, a link somebody sent. Renaming it changes what they all have to say."
              label="Name"
              initialValue={project}
              submitLabel="Rename"
              onSubmit={async (name) => {
                await answered(
                  api.PATCH("/api/v1/projects/{project}", {
                    params: { path: { project } },
                    body: { name },
                  }),
                );

                // The address carries the name, so a renamed project lives at a
                // different one. The reader is taken there rather than left at an
                // address that no longer names anything, and `replace` keeps the
                // old name out of the history behind them.
                void navigate(projectPath(name), { replace: true });
              }}
            />
          </>
        )}
      </PageHeader>

      <div className="flex flex-1 flex-col gap-3 p-4">
        {asked.at === "asking" && <Rows />}
        {asked.at === "refused" && <Refusal>{asked.why}</Refusal>}
        {asked.at === "answered" && (
          <Environments project={asked.data} showingDeleted={deleted} onChanged={again} />
        )}
      </div>
    </>
  );
}

function Environments({
  project,
  showingDeleted,
  onChanged,
}: {
  project: Project;
  showingDeleted: boolean;
  onChanged: () => void;
}) {
  const showing = project.environments.filter(
    (environment) => (environment.deletedAt !== null) === showingDeleted,
  );

  if (showing.length === 0) {
    return (
      <p className="rounded-lg border border-dashed px-3 py-10 text-center text-sm text-muted-foreground">
        {showingDeleted
          ? "No environment of this project has been deleted in the last 72 hours."
          : "This project has no environments. An environment is where its keys live."}
      </p>
    );
  }

  return (
    <ul className="divide-y rounded-lg border">
      {showing.map((environment) => (
        <EnvironmentRow
          key={environment.id}
          project={project.name}
          environment={environment}
          onChanged={onChanged}
        />
      ))}
    </ul>
  );
}

function EnvironmentRow({
  project,
  environment,
  onChanged,
}: {
  project: string;
  environment: Environment;
  onChanged: () => void;
}) {
  const gone = environment.deletedAt !== null;

  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2.5">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline gap-x-2">
          {gone ? (
            <span className="font-mono text-sm">{environment.name}</span>
          ) : (
            <Link
              className="font-mono text-sm font-medium hover:underline"
              to={environmentPath(project, environment.name)}
            >
              {environment.name}
            </Link>
          )}
          {gone && (
            <span className="text-xs text-destructive" title={when(environment.deletedAt)}>
              deleted {around(environment.deletedAt)}
            </span>
          )}
        </div>
        {!gone && <Summary project={project} environment={environment.name} />}
      </div>

      <div className="flex items-center gap-2">
        {gone ? (
          <>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                void answered(
                  api.POST("/api/v1/projects/{project}/environments/{environment}/restore", {
                    params: { path: { project, environment: environment.name } },
                  }),
                ).then(onChanged);
              }}
            >
              Restore
            </Button>
            <PurgeDialog
              trigger={
                <Button variant="ghost" size="sm">
                  Purge
                </Button>
              }
              title={`Purge ${environment.name}?`}
              what="It removes this deleted environment from the instance now, together with its keys and every value they hold, and frees the name inside this project."
              onConfirm={async () => {
                await answered(
                  api.POST("/api/v1/projects/{project}/environments/{environment}/purge", {
                    params: { path: { project, environment: environment.name } },
                  }),
                );

                onChanged();
              }}
            />
          </>
        ) : (
          <>
            <TextActionDialog
              trigger={
                <Button variant="ghost" size="sm">
                  Rename
                </Button>
              }
              title={`Rename ${environment.name}`}
              description="An environment is named inside every reference to a key in it, so anything pointing here has to be told."
              label="Name"
              initialValue={environment.name}
              submitLabel="Rename"
              onSubmit={async (name) => {
                await answered(
                  api.PATCH("/api/v1/projects/{project}/environments/{environment}", {
                    params: { path: { project, environment: environment.name } },
                    body: { name },
                  }),
                );

                onChanged();
              }}
            />
            <ActionDialog
              trigger={
                <Button variant="ghost" size="sm">
                  Delete
                </Button>
              }
              title={`Delete ${environment.name}?`}
              description="It leaves every listing and stops working at once, and it can be restored for 72 hours with its keys and their values as they are now. Anything reading a key from it — an application, an agent, a pipeline — stops finding one."
              confirmLabel="Delete"
              onConfirm={async () => {
                await answered(
                  api.DELETE("/api/v1/projects/{project}/environments/{environment}", {
                    params: { path: { project, environment: environment.name } },
                  }),
                );

                onChanged();
              }}
            />
          </>
        )}
      </div>
    </li>
  );
}

/**
 * How many keys are in an environment, how many of them are waiting for a
 * person, and when one was last written. Names and status only — this is the
 * listing endpoint, and there is no value in it to summarise.
 */
function Summary({ project, environment }: { project: string; environment: string }) {
  const [asked] = useAsk<Secret[]>(`secrets:${project}/${environment}`, () =>
    api.GET("/api/v1/projects/{project}/environments/{environment}/secrets", {
      params: { path: { project, environment } },
    }),
  );

  if (asked.at !== "answered") {
    return <span className="text-xs text-muted-foreground">{asked.at === "asking" ? "…" : ""}</span>;
  }

  const empty = asked.data.filter((secret) => secret.status === "empty").length;

  const written = asked.data
    .map((secret) => secret.valueWrittenAt)
    .filter((moment): moment is string => moment != null)
    .sort()
    .at(-1);

  return (
    <p className="text-xs text-muted-foreground">
      {asked.data.length === 0 ? "no keys yet" : `${asked.data.length} keys`}
      {empty > 0 && ` · ${empty} waiting for a value`}
      {written !== undefined && (
        <span title={when(written)}> · last written {around(written)}</span>
      )}
    </p>
  );
}

function NewEnvironment({ project, onCreated }: { project: string; onCreated: () => void }) {
  return (
    <TextActionDialog
      trigger={
        <Button size="sm">
          <PlusIcon />
          New environment
        </Button>
      }
      title={`A new environment in ${project}`}
      description="Lower case and narrow, because the name goes inside a vaultaffe:// reference. An extra environment offers no privacy: everybody in the organization sees it."
      label="Name"
      initialValue=""
      submitLabel="Create"
      onSubmit={async (name) => {
        await answered(
          api.POST("/api/v1/projects/{project}/environments", {
            params: { path: { project } },
            body: { name: name.toLowerCase() },
          }),
        );

        onCreated();
      }}
    />
  );
}
