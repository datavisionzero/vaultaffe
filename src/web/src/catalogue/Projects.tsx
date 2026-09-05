import { PlusIcon } from "lucide-react";
import { useState, type FormEvent } from "react";
import { Link } from "react-router";
import { answered, api, describe, type Project } from "@/api/client";
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
import { ActionDialog, TextActionDialog } from "@/shared/ActionDialog";
import { Field, Refusal } from "@/shared/Form";
import { around, when } from "@/shared/moments";
import { PageHeader } from "@/shared/PageHeader";
import { PurgeDialog } from "@/shared/Purge";
import { Rows } from "@/shared/Rows";
import { environmentPath, projectPath } from "@/shell/views";
import { Deleted } from "./Deleted";

/**
 * `/projects` — every project this session reaches, each with its environments,
 * and the switch for what is deleted and recoverable
 * (`docs/human-interface.md`).
 *
 * A session reaches the whole organization, so this is every project of it; a
 * token's binding narrows the same listing, which is the instance's business and
 * not this screen's. Nothing here shows a value, and no request made from this
 * screen could return one — names and values are two endpoints
 * ([`api.md`](../../../../docs/api.md)).
 */
export function Projects() {
  const [deleted, setDeleted] = useState(false);
  const [projects, again] = useAsk<Project[]>(`projects:${deleted}`, () =>
    api.GET("/api/v1/projects", { params: { query: { deleted } } }),
  );

  return (
    <>
      <PageHeader title="Projects">
        <Deleted showing={deleted} onShowing={setDeleted} />
        <NewProject onCreated={again} />
      </PageHeader>

      <div className="flex flex-1 flex-col gap-3 p-4">
        {projects.at === "asking" && <Rows />}
        {projects.at === "refused" && <Refusal>{projects.why}</Refusal>}
        {projects.at === "answered" &&
          (projects.data.length === 0 ? (
            <p className="rounded-lg border border-dashed px-3 py-10 text-center text-sm text-muted-foreground">
              {deleted
                ? "Nothing here has been deleted in the last 72 hours."
                : "No projects yet. A project is an application or a service, and it arrives with its environments."}
            </p>
          ) : (
            <ul className="grid gap-3">
              {projects.data.map((project) => (
                <ProjectCard key={project.id} project={project} onChanged={again} />
              ))}
            </ul>
          ))}
      </div>
    </>
  );
}

/** One project, with its environments as chips that wrap on a narrow screen. */
function ProjectCard({ project, onChanged }: { project: Project; onChanged: () => void }) {
  const gone = project.deletedAt !== null;

  return (
    <li className="rounded-lg border">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b px-3 py-2.5">
        <h2 className="text-sm font-medium">
          {gone ? (
            <span className="font-mono">{project.name}</span>
          ) : (
            <Link className="font-mono hover:underline" to={projectPath(project.name)}>
              {project.name}
            </Link>
          )}
        </h2>
        {/* Deleted is a word, and the moment it happened is what says how long
            there is left to change one's mind. */}
        {gone && (
          <span className="text-xs text-destructive" title={when(project.deletedAt)}>
            deleted {around(project.deletedAt)}
          </span>
        )}
        <div className="ml-auto flex items-center gap-2">
          {gone ? (
            <>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  void answered(
                    api.POST("/api/v1/projects/{project}/restore", {
                      params: { path: { project: project.name } },
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
                title={`Purge ${project.name}?`}
                what="It removes this deleted project from the instance now, together with the environments, keys and values kept under it, and frees the name for something else. The change log keeps what happened here — it records names rather than ids so that it can."
                onConfirm={async () => {
                  await answered(
                    api.POST("/api/v1/projects/{project}/purge", {
                      params: { path: { project: project.name } },
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
                title={`Rename ${project.name}`}
                description="Every reference to this project is by name — the CLI's binding, a vaultaffe:// reference, a link somebody sent. Renaming it changes what they all have to say."
                label="Name"
                initialValue={project.name}
                submitLabel="Rename"
                onSubmit={async (name) => {
                  await answered(
                    api.PATCH("/api/v1/projects/{project}", {
                      params: { path: { project: project.name } },
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
                title={`Delete ${project.name}?`}
                description="It leaves every listing and stops working at once, and it can be restored for 72 hours. Its environments and their values are kept with it and come back as they were; the name stays reserved for as long as the project can come back."
                confirmLabel="Delete"
                onConfirm={async () => {
                  await answered(
                    api.DELETE("/api/v1/projects/{project}", {
                      params: { path: { project: project.name } },
                    }),
                  );

                  onChanged();
                }}
              />
            </>
          )}
        </div>
      </div>

      <div className="flex flex-wrap gap-1.5 px-3 py-2.5">
        {project.environments.length === 0 ? (
          <span className="text-xs text-muted-foreground">No environments in this project.</span>
        ) : (
          project.environments.map((environment) => (
            <Link
              key={environment.id}
              to={environmentPath(project.name, environment.name)}
              className="rounded-md border px-2 py-1 font-mono text-xs hover:bg-accent"
            >
              {environment.name}
              {environment.deletedAt !== null && (
                <span className="ml-1.5 text-destructive">deleted</span>
              )}
            </Link>
          ))
        )}
      </div>
    </li>
  );
}

/**
 * Creating a project, and its environments with it.
 *
 * `dev`, `staging` and `prod` unless the person names others — the instance's
 * own default, offered here as the text it will use rather than as a hidden one,
 * so that somebody who wants two environments or five can say so before the
 * project exists ([`api.md`](../../../../docs/api.md)).
 */
function NewProject({ onCreated }: { onCreated: () => void }) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [environments, setEnvironments] = useState("dev, staging, prod");
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();

  function change(next: boolean) {
    setOpen(next);

    if (!next) {
      setName("");
      setEnvironments("dev, staging, prod");
      setRefusal(undefined);
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST("/api/v1/projects", {
        body: {
          name,
          environments: environments
            .split(",")
            .map((one) => one.trim())
            .filter((one) => one !== ""),
        },
      });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      change(false);
      onCreated();
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Button size="sm" onClick={() => change(true)}>
        <PlusIcon />
        New project
      </Button>

      <Dialog open={open} onOpenChange={change}>
        <DialogContent>
          <form className="grid gap-4" onSubmit={(event) => void submit(event)}>
            <DialogHeader>
              <DialogTitle>A new project</DialogTitle>
              <DialogDescription>
                A project is an application or a service, and it is created with its environments.
              </DialogDescription>
            </DialogHeader>
            <Field
              label="Name"
              autoFocus
              required
              value={name}
              onChange={(event) => setName(event.target.value.toLowerCase())}
              hint="Lower case, narrow: the name goes inside a vaultaffe:// reference."
            />
            <Field
              label="Environments"
              value={environments}
              onChange={(event) => setEnvironments(event.target.value.toLowerCase())}
              hint="Separated by commas. Leave it empty to create the project with none."
            />
            {refusal !== undefined && <Refusal>{refusal}</Refusal>}
            <DialogFooter>
              <Button variant="outline" disabled={busy} onClick={() => change(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={busy || name.trim() === ""}>
                {busy ? "Creating…" : "Create"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}
