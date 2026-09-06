import type { Change } from "@/api/client";
import { around, when } from "@/shared/moments";

/**
 * The entries of the change log, wherever they are read: the whole
 * organization's on `/changes`, and one key's own on its screen.
 *
 * **An entry carries no value, not even as a diff**
 * ([Specification §6.5](../../../../Specification.md#65-logging-and-history)).
 * It is a moment, an action, the names it happened to, and the acting identity
 * **with its type** — and that last field is what the log exists for: with
 * writing agents the interesting question is not only who acted but what kind
 * of thing did. So the type is a word on the row rather than an icon or a
 * colour, and it is never dropped to save space.
 *
 * The names are text and not links. The log records names rather than ids
 * precisely so that it can still say what happened to something that no longer
 * exists ([`api.md`](../../../../docs/api.md)), and a link is a promise that
 * something is still there.
 *
 * **An entry with no place is an administrative one** — a password set, an
 * address changed, a token issued — and what it happened to is in `about`
 * ([ADR 0020](../../../../docs/adr/0020-one-change-log-and-not-two.md)). It is
 * shown in the same column as a place, because a reader scanning this list is
 * asking "what was touched", and whether the answer is a key or a person is
 * already said by the action beside it.
 */
export function Entries({ entries, place = true }: { entries: Change[]; place?: boolean }) {
  return (
    <ul className="divide-y rounded-lg border">
      {entries.map((entry) => (
        <li key={entry.id} className="flex flex-wrap items-baseline gap-x-3 gap-y-1 px-3 py-2.5">
          <span className="rounded-sm border px-1 font-mono text-[11px]">{entry.action}</span>
          {place && <span className="font-mono text-sm break-all">{where(entry)}</span>}
          <span className="text-xs text-muted-foreground">
            by <span className="text-foreground">{entry.identity.name}</span>{" "}
            <span className="rounded-sm border border-dashed px-1">{entry.identity.type}</span>
          </span>
          <span
            className="ml-auto text-xs text-muted-foreground"
            title={when(entry.occurredAt)}
          >
            {around(entry.occurredAt)}
          </span>
        </li>
      ))}
    </ul>
  );
}

/**
 * What an entry happened to: a place in the vault, in the same
 * `project/environment/KEY` spelling the CLI prints and an address uses, or the
 * person, token or organization an administrative entry was about.
 *
 * An entry with neither is the organization's own and says so. That is rare and
 * deliberately not blank: a row a reader cannot place is worse than one that
 * says it has no place.
 */
function where(change: Change): string {
  const parts = [change.project, change.environment, change.secret].filter(
    (name): name is string => name != null && name !== "",
  );

  if (parts.length > 0) {
    return parts.join("/");
  }

  return change.about != null && change.about !== "" ? change.about : "the organization";
}
