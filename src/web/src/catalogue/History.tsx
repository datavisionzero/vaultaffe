import { useState } from "react";
import { answered, api, type ChangePage, type Purged, type Secret, type Version } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Entries } from "@/changes/Entries";
import { ActionDialog } from "@/shared/ActionDialog";
import { Done, Refusal } from "@/shared/Form";
import { around, when } from "@/shared/moments";
import { PurgeDialog } from "@/shared/Purge";

/**
 * What a key used to hold, and this key's own change log — the second half of
 * its screen (`docs/human-interface.md`).
 *
 * The two are read together and are not the same thing
 * ([Specification §6.5](../../../../Specification.md#65-logging-and-history)).
 * The **history** holds a few superseded values under tight bounds and hands
 * none of them out: the listing is ids and moments, because five old
 * credentials in one answer would be the bulk disclosure the rest of this
 * product spends every decision avoiding. The **log** holds no value at all.
 * Neither is ever a place a value appears on this screen; the one act that
 * fetches one is the reveal, above.
 *
 * Both are re-read after an act rather than patched in place: a rollback moves
 * a version out of the history and writes an entry into the log, and a client
 * keeping its own copy of either is a client that can disagree with the
 * instance.
 */
export function History({
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
  const [acts, setActs] = useState(0);
  const [purged, setPurged] = useState<Purged>();
  const stamp = `${project}/${environment}/${secret.name}:${secret.valueWrittenAt ?? ""}:${acts}`;

  const [versions] = useAsk<Version[]>(`versions:${stamp}`, () =>
    api.GET(
      "/api/v1/projects/{project}/environments/{environment}/secrets/{name}/versions",
      { params: { path: { project, environment, name: secret.name } } },
    ),
  );

  function acted() {
    setActs((count) => count + 1);
    onChanged();
  }

  return (
    <div className="grid max-w-lg gap-6">
      <section className="grid gap-3">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="text-sm font-medium">What it used to hold</h2>
          {versions.at === "answered" && versions.data.length > 0 && (
            <PurgeDialog
              trigger={
                <Button variant="ghost" size="sm">
                  Purge the history
                </Button>
              }
              title={`Purge the history of ${secret.name}?`}
              what={`It removes every value this key used to hold, and keeps the key and the value it holds now. After a suspected compromise this is the request that makes an old credential genuinely gone from this instance.`}
              onConfirm={async () => {
                setPurged(
                  await answered(
                    api.DELETE(
                      "/api/v1/projects/{project}/environments/{environment}/secrets/{name}/versions",
                      { params: { path: { project, environment, name: secret.name } } },
                    ),
                  ),
                );

                acted();
              }}
            />
          )}
        </div>

        <p className="text-xs text-muted-foreground">
          Five versions or 72 hours, whichever comes first. When each was written and when it goes —
          never what it was.
        </p>

        {versions.at === "asking" && <Skeleton className="h-16 w-full" />}
        {versions.at === "refused" && <Refusal>{versions.why}</Refusal>}
        {versions.at === "answered" &&
          (versions.data.length === 0 ? (
            <p className="rounded-lg border border-dashed px-3 py-6 text-center text-sm text-muted-foreground">
              This key has held nothing else — or what it did has already passed both bounds.
            </p>
          ) : (
            <ul className="divide-y rounded-lg border">
              {versions.data.map((version) => (
                <VersionRow
                  key={version.id}
                  project={project}
                  environment={environment}
                  name={secret.name}
                  version={version}
                  onRolledBack={acted}
                />
              ))}
            </ul>
          ))}

        {purged !== undefined && (
          <Done>
            {purged.versions === 1
              ? "One earlier value is gone from this instance."
              : `${purged.versions} earlier values are gone from this instance.`}{" "}
            Last night's backup still has them.
          </Done>
        )}
      </section>

      <Trail project={project} environment={environment} name={secret.name} stamp={stamp} />
    </div>
  );
}

/**
 * One superseded value: when it was written, when it stopped being current, and
 * when it goes.
 *
 * **Rolling back is a write like any other**, which is why it is offered here
 * rather than guarded: an agent that wrecked a value overnight is exactly who
 * needs an undo button ([`api.md`](../../../../docs/api.md)). It asks first all
 * the same, because it overwrites the value that is current now — and
 * overwriting is as destructive as deleting.
 */
function VersionRow({
  project,
  environment,
  name,
  version,
  onRolledBack,
}: {
  project: string;
  environment: string;
  name: string;
  version: Version;
  onRolledBack: () => void;
}) {
  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2.5">
      <div className="min-w-0 flex-1">
        <p className="text-sm">
          <span title={when(version.writtenAt)}>written {around(version.writtenAt)}</span>
          <span className="text-muted-foreground">
            {" · "}
            <span title={when(version.replacedAt)}>replaced {around(version.replacedAt)}</span>
          </span>
        </p>
        <p className="text-xs text-muted-foreground" title={when(version.expiresAt)}>
          kept until {when(version.expiresAt)}
        </p>
      </div>

      <ActionDialog
        trigger={
          <Button variant="outline" size="sm">
            Roll back to this
          </Button>
        }
        title={`Roll back ${name}?`}
        description="This key holds what it held at that moment again, and the value it holds now takes that version's place in the history. Anything reading the key gets the older value from here on, and the change log says it was rolled back."
        confirmLabel="Roll back"
        onConfirm={async () => {
          await answered(
            api.POST(
              "/api/v1/projects/{project}/environments/{environment}/secrets/{name}/rollback",
              {
                params: { path: { project, environment, name } },
                body: { versionId: version.id },
              },
            ),
          );

          onRolledBack();
        }}
      />
    </li>
  );
}

/**
 * This key's own change log: the same entries as `/changes`, narrowed to one
 * key by the instance rather than by this screen. The names are the filter the
 * endpoint takes, and they are the three this address already carries.
 */
function Trail({
  project,
  environment,
  name,
  stamp,
}: {
  project: string;
  environment: string;
  name: string;
  stamp: string;
}) {
  const [asked] = useAsk<ChangePage>(`changes:${stamp}`, () =>
    api.GET("/api/v1/changes", {
      params: { query: { project, environment, secret: name, limit: 20 } },
    }),
  );

  return (
    <section className="grid gap-3">
      <h2 className="text-sm font-medium">What happened to it</h2>

      {asked.at === "asking" && <Skeleton className="h-16 w-full" />}
      {asked.at === "refused" && <Refusal>{asked.why}</Refusal>}
      {asked.at === "answered" &&
        (asked.data.entries.length === 0 ? (
          <p className="rounded-lg border border-dashed px-3 py-6 text-center text-sm text-muted-foreground">
            Nothing has been changed about this key yet.
          </p>
        ) : (
          <>
            <Entries entries={asked.data.entries} place={false} />
            {asked.data.total > asked.data.entries.length && (
              <p className="text-xs text-muted-foreground">
                The {asked.data.entries.length} most recent of {asked.data.total}. The change log has
                the rest.
              </p>
            )}
          </>
        ))}
    </section>
  );
}
