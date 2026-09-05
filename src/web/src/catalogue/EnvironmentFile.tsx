import { DownloadIcon, UploadIcon } from "lucide-react";
import { useState, type DragEvent } from "react";
import { api, describe, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Copyable } from "@/shared/Copyable";
import { Refusal } from "@/shared/Form";

type Import = Schemas["Import"];

/**
 * Import and export live on the environment screen because they are per
 * environment (`docs/human-interface.md`), and they are the two halves of the
 * pragmatic migration path: the way in from `.env` files, and the honest way
 * back out.
 */

/**
 * Import: a `.env` pasted or dropped, applied all of it or none of it.
 *
 * **The answer names keys and never values** — created, filled, replaced,
 * unchanged, skipped with a reason, unreadable with a line number — which is
 * what lets an agent migrate a file it never displays
 * ([`api.md`](../../../../docs/api.md)). This screen shows exactly that answer,
 * and the file itself never leaves the browser except as the request body.
 */
export function ImportDialog({
  project,
  environment,
  onImported,
}: {
  project: string;
  environment: string;
  onImported: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [content, setContent] = useState("");
  const [replace, setReplace] = useState(false);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [outcome, setOutcome] = useState<Import>();

  function change(next: boolean) {
    setOpen(next);

    if (!next) {
      setContent("");
      setReplace(false);
      setRefusal(undefined);
      setOutcome(undefined);
    }
  }

  async function drop(event: DragEvent<HTMLTextAreaElement>) {
    event.preventDefault();

    const file = event.dataTransfer.files[0];

    if (file !== undefined) {
      setContent(await file.text());
    }
  }

  async function send() {
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST(
        "/api/v1/projects/{project}/environments/{environment}/import",
        { params: { path: { project, environment } }, body: { content, replace } },
      );

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      setOutcome(data);
      onImported();
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Button variant="outline" size="sm" onClick={() => change(true)}>
        <UploadIcon />
        Import
      </Button>

      <Dialog open={open} onOpenChange={change}>
        <DialogContent className="sm:max-w-lg">
          {outcome === undefined ? (
            <div className="grid gap-4">
              <DialogHeader>
                <DialogTitle>
                  Import a .env into {project}/{environment}
                </DialogTitle>
                <DialogDescription>
                  Paste the file, or drop it here. All of it is applied or none of it is, and what
                  comes back names keys and never values.
                </DialogDescription>
              </DialogHeader>

              <textarea
                name="content"
                aria-label="The contents of a .env file"
                className="h-48 w-full rounded-lg border bg-transparent p-2 font-mono text-xs outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
                placeholder={"DATABASE_URL=postgres://…\n# a comment\nSMTP_PASSWORD="}
                value={content}
                onChange={(event) => setContent(event.target.value)}
                onDragOver={(event) => event.preventDefault()}
                onDrop={(event) => void drop(event)}
              />

              <label className="flex items-start gap-2 text-sm">
                <input
                  type="checkbox"
                  name="replace"
                  className="mt-0.5 size-4 accent-primary"
                  checked={replace}
                  onChange={(event) => setReplace(event.target.checked)}
                />
                <span>
                  Overwrite keys that already hold a value
                  <span className="block text-xs text-muted-foreground">
                    Without this, a key that holds a value is skipped and said to have been. An
                    empty placeholder is filled either way.
                  </span>
                </span>
              </label>

              {refusal !== undefined && <Refusal>{refusal}</Refusal>}

              <DialogFooter>
                <Button variant="outline" disabled={busy} onClick={() => change(false)}>
                  Cancel
                </Button>
                <Button disabled={busy || content.trim() === ""} onClick={() => void send()}>
                  {busy ? "Importing…" : replace ? "Import and overwrite" : "Import"}
                </Button>
              </DialogFooter>
            </div>
          ) : (
            <div className="grid gap-4">
              <DialogHeader>
                <DialogTitle>What the file did</DialogTitle>
                <DialogDescription>
                  Names, and no values — the same answer the CLI and an agent get.
                </DialogDescription>
              </DialogHeader>

              <dl className="grid gap-2 text-sm">
                <Named what="Created" names={outcome.created} />
                <Named what="Filled" names={outcome.filled} />
                <Named what="Replaced" names={outcome.replaced} />
                <Named what="Unchanged" names={outcome.unchanged} />
                <Named
                  what="Skipped"
                  names={outcome.skipped.map((one) => `${one.name} — ${one.reason}`)}
                />
                <Named
                  what="Unreadable"
                  names={outcome.unreadable.map((one) => `line ${one.line} — ${one.reason}`)}
                />
              </dl>

              <p className="text-xs text-muted-foreground">
                If this file lived on a laptop or in a repository, it is now in two places. Deleting
                the file is the other half of the migration.
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

function Named({ what, names }: { what: string; names: string[] }) {
  if (names.length === 0) {
    return null;
  }

  return (
    <div className="grid grid-cols-[7rem_1fr] gap-2">
      <dt className="text-xs text-muted-foreground">
        {what} ({names.length})
      </dt>
      <dd className="font-mono text-xs break-all">{names.join(", ")}</dd>
    </div>
  );
}

/**
 * Export: every value of the environment, in plaintext, for a person.
 *
 * It is the plainest screen in the application and the one that asks the hardest
 * question. **It always asks**, even of an administrator doing it weekly, and it
 * is the one confirmation here that does not offer "don't ask again"
 * (`docs/human-interface.md`). The refusal an agent gets is `human-only`, and
 * that is the instance's, not this dialog's: a token carrying every scope there
 * is still cannot do this.
 */
export function ExportDialog({ project, environment }: { project: string; environment: string }) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [file, setFile] = useState<string>();

  function change(next: boolean) {
    setOpen(next);

    if (!next) {
      // The plaintext goes when the dialog does. Nothing about an export is kept
      // in this tab, and nothing about it reaches `sessionStorage`.
      setFile(undefined);
      setRefusal(undefined);
    }
  }

  async function write() {
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.GET(
        "/api/v1/projects/{project}/environments/{environment}/export",
        { params: { path: { project, environment } }, parseAs: "text" },
      );

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      setFile(data);
    } finally {
      setBusy(false);
    }
  }

  function save() {
    const url = URL.createObjectURL(new Blob([file ?? ""], { type: "text/plain" }));
    const link = document.createElement("a");

    link.href = url;
    link.download = `${project}.${environment}.env`;
    link.click();

    URL.revokeObjectURL(url);
  }

  return (
    <>
      <Button variant="outline" size="sm" onClick={() => change(true)}>
        <DownloadIcon />
        Export
      </Button>

      <Dialog open={open} onOpenChange={change}>
        <DialogContent>
          {file === undefined ? (
            <div className="grid gap-4">
              <DialogHeader>
                <DialogTitle>
                  Export {project}/{environment}?
                </DialogTitle>
                <DialogDescription>
                  This writes <strong>every value of this environment in plaintext</strong> — the
                  one thing the rest of this product spends every decision avoiding. It exists
                  because a way back out is part of being trustworthy, and it is a person's alone:
                  no token, however many scopes it carries, can do it.
                </DialogDescription>
              </DialogHeader>
              <p className="text-sm text-muted-foreground">
                Whatever you save it to is a file full of live credentials. Nothing here can take it
                back afterwards.
              </p>
              {refusal !== undefined && <Refusal>{refusal}</Refusal>}
              <DialogFooter>
                <Button variant="outline" disabled={busy} onClick={() => change(false)}>
                  Cancel
                </Button>
                <Button variant="destructive" disabled={busy} onClick={() => void write()}>
                  {busy ? "Exporting…" : "Export in plaintext"}
                </Button>
              </DialogFooter>
            </div>
          ) : (
            <div className="grid gap-4">
              <DialogHeader>
                <DialogTitle>
                  {project}.{environment}.env
                </DialogTitle>
                <DialogDescription>
                  Here it is. It is on this screen and nowhere else until you save or copy it, and
                  it is gone from this tab when this dialog closes.
                </DialogDescription>
              </DialogHeader>
              {/* Not shown: an environment of values on screen is a shoulder
                  away from being somebody else's. The two ways out of here put
                  it where it was asked for instead. */}
              <p className="text-sm text-muted-foreground">
                {file.split("\n").filter((line) => line.trim() !== "").length} lines, and every one
                of them a value.
              </p>
              <Copyable value={file} label="export" hidden />
              <DialogFooter>
                <Button variant="outline" onClick={() => change(false)}>
                  Close
                </Button>
                <Button onClick={save}>Save the file</Button>
              </DialogFooter>
            </div>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
}
