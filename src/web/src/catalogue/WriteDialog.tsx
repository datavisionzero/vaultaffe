import { useState, type FormEvent, type ReactElement } from "react";
import { api, describe, type Problem } from "@/api/client";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Field, Refusal } from "@/shared/Form";

/**
 * Creating a key and writing a value are the same form (`docs/human-interface.md`):
 * a name, and a value the form does not echo back after it is saved.
 *
 * Three rules of the instance are visible in it and none of them are decided
 * here ([`api.md`](../../../../docs/api.md)):
 *
 * - **An empty placeholder is asked for, not typed.** `{"value": null}` is the
 *   state that means a person still has to do something; an empty *string* is
 *   refused, because one standing in for the other is the bug the placeholder
 *   exists to prevent.
 * - **Overwriting is explicit.** A value over a key that already holds one is
 *   refused with `replace-required`, and this dialog turns that refusal into the
 *   question it is rather than retrying quietly. Filling a placeholder asks
 *   nothing, and neither does the instance.
 * - **Nothing is echoed back.** The answer carries no value, and this form
 *   forgets what was typed the moment it is closed.
 */
export function WriteDialog({
  project,
  environment,
  name: fixed,
  trigger,
  onWritten,
}: {
  project: string;
  environment: string;
  /** The key, when the screen already knows which one is being written. */
  name?: string;
  trigger: ReactElement;
  onWritten: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState(fixed ?? "");
  const [value, setValue] = useState("");
  const [placeholder, setPlaceholder] = useState(false);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [asking, setAsking] = useState<string>();

  function change(next: boolean) {
    setOpen(next);

    if (!next) {
      setName(fixed ?? "");
      setValue("");
      setPlaceholder(false);
      setRefusal(undefined);
      setAsking(undefined);
    }
  }

  async function write(replace: boolean) {
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.PUT(
        "/api/v1/projects/{project}/environments/{environment}/secrets/{name}",
        {
          params: { path: { project, environment, name } },
          body: { value: placeholder ? null : value, replace },
        },
      );

      if (data === undefined) {
        const problem = error as Problem | undefined;

        // The one refusal that is a question rather than an error: the key holds
        // a value, and overwriting one is as destructive as deleting it.
        if (problem?.code === "replace-required") {
          setAsking(problem.detail ?? "That key already holds a value.");
          return;
        }

        setRefusal(describe(problem, response.status));
        return;
      }

      change(false);
      onWritten();
    } finally {
      setBusy(false);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void write(false);
  }

  return (
    <Dialog open={open} onOpenChange={change}>
      <DialogTrigger render={trigger} />
      <DialogContent>
        {asking === undefined ? (
          <form className="grid gap-4" onSubmit={submit}>
            <DialogHeader>
              <DialogTitle>{fixed === undefined ? "A new key" : `Write ${fixed}`}</DialogTitle>
              <DialogDescription>
                In {project}/{environment}. The value is not shown again after it is saved — not
                here, and not in any answer this instance gives.
              </DialogDescription>
            </DialogHeader>

            {fixed === undefined && (
              <Field
                label="Key"
                autoFocus
                required
                value={name}
                onChange={(event) => setName(event.target.value.toUpperCase())}
                hint="Upper case, as an environment variable is."
                className="font-mono"
              />
            )}

            <Field
              label="Value"
              type="password"
              autoComplete="off"
              autoFocus={fixed !== undefined}
              required={!placeholder}
              disabled={placeholder}
              value={placeholder ? "" : value}
              onChange={(event) => setValue(event.target.value)}
              className="font-mono"
            />

            <label className="flex items-start gap-2 text-sm">
              <input
                type="checkbox"
                name="placeholder"
                className="mt-0.5 size-4 accent-primary"
                checked={placeholder}
                onChange={(event) => setPlaceholder(event.target.checked)}
              />
              <span>
                Leave it empty for somebody to fill
                <span className="block text-xs text-muted-foreground">
                  An empty placeholder is a key that exists and holds nothing. It is what stops an
                  application from starting with a value nobody meant, and it is how an agent
                  prepares work for a person.
                </span>
              </span>
            </label>

            {refusal !== undefined && <Refusal>{refusal}</Refusal>}

            <DialogFooter>
              <Button variant="outline" disabled={busy} onClick={() => change(false)}>
                Cancel
              </Button>
              <Button
                type="submit"
                disabled={busy || name.trim() === "" || (!placeholder && value === "")}
              >
                {busy ? "Saving…" : placeholder ? "Create the placeholder" : "Save the value"}
              </Button>
            </DialogFooter>
          </form>
        ) : (
          <div className="grid gap-4">
            <DialogHeader>
              <DialogTitle>Overwrite {name}?</DialogTitle>
              <DialogDescription>
                {asking} What it holds now is kept as a version for 72 hours and can be rolled back
                to; after that it is gone. This is what a rotation looks like in the change log.
              </DialogDescription>
            </DialogHeader>
            {refusal !== undefined && <Refusal>{refusal}</Refusal>}
            <DialogFooter>
              <Button variant="outline" disabled={busy} onClick={() => setAsking(undefined)}>
                Cancel
              </Button>
              <Button variant="destructive" disabled={busy} onClick={() => void write(true)}>
                {busy ? "Overwriting…" : "Overwrite it"}
              </Button>
            </DialogFooter>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
