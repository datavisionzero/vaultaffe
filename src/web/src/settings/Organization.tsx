import { useState, type FormEvent } from "react";
import { api, describe, type Schemas } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useSession } from "@/session/useSession";
import { Done, Field, Refusal } from "@/shared/Form";
import { when } from "@/shared/moments";

type Organization = Schemas["Organization"];

/**
 * `/settings/organization` — the organization's name, and that is the whole
 * screen (`docs/human-interface.md`).
 *
 * There is one organization in the MVP, called Default until a team says what
 * they call themselves ([Specification §6.1](../../../../Specification.md#61-web-ui)).
 * Renaming it is an administrator's; a person who is not one sees the field with
 * the reason beside it rather than not at all.
 */
export function Organization() {
  const { me } = useSession();
  const [asked, again] = useAsk<Organization>("organization", () =>
    api.GET("/api/v1/organization"),
  );

  return (
    <div className="grid max-w-lg gap-3">
      <div>
        <h2 className="text-sm font-semibold">Organization</h2>
        <p className="text-xs text-muted-foreground">
          The one thing there is to say about it. Nothing refers to an organization by name — a
          reference names a project and an environment — so renaming it changes a word and nothing
          else.
        </p>
      </div>

      {asked.at === "asking" && <Skeleton className="h-16 w-full" />}
      {asked.at === "refused" && <Refusal>{asked.why}</Refusal>}
      {asked.at === "answered" && (
        <Name organization={asked.data} administrator={me.isAdministrator} onRenamed={again} />
      )}
    </div>
  );
}

function Name({
  organization,
  administrator,
  onRenamed,
}: {
  organization: Organization;
  administrator: boolean;
  onRenamed: () => void;
}) {
  const [name, setName] = useState(organization.name);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [renamed, setRenamed] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);
    setRenamed(false);

    try {
      const { data, error, response } = await api.PATCH("/api/v1/organization", { body: { name } });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      setRenamed(true);
      onRenamed();
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="grid gap-3 rounded-lg border p-4" onSubmit={(event) => void submit(event)}>
      <Field
        label="Name"
        required
        maxLength={100}
        disabled={!administrator}
        value={name}
        onChange={(event) => setName(event.target.value)}
        hint={
          administrator
            ? `Created ${when(organization.createdAt)}.`
            : "Renaming the organization is an administrator's. Ask one."
        }
      />

      {refusal !== undefined && <Refusal>{refusal}</Refusal>}
      {renamed && <Done>Renamed.</Done>}

      {administrator && (
        <div>
          <Button type="submit" disabled={busy || name.trim() === "" || name === organization.name}>
            {busy ? "Renaming…" : "Rename"}
          </Button>
        </div>
      )}
    </form>
  );
}
