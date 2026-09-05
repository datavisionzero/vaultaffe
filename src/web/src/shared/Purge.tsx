import type { ReactElement } from "react";
import { ActionDialog } from "./ActionDialog";

/**
 * The confirmation a purge gets, and the two sentences nobody else says
 * (`docs/human-interface.md`, [`api.md`](../../../../docs/api.md)).
 *
 * There are four purges — a project, an environment, a secret, and what a
 * secret used to hold — and the thing that makes them one act rather than four
 * is what they say. The first sentence belongs to the purge that is being asked
 * for. The other two are the same everywhere, and they are in the dialog rather
 * than in the small print, because the reader deciding is the only person who
 * can act on either of them:
 *
 * - **It destroys the undo button.** A purge is the second half of a deletion
 *   and not a faster one, which is also why it is refused on something still in
 *   use.
 * - **It does not reach last night's backup.** This product promises backups
 *   ([Specification §6.3](../../../../Specification.md#63-operations)), so a
 *   value that has to be gone everywhere is a job the backups are part of and
 *   no button here can finish it. No product we looked at says this out loud; it
 *   is true of all of them.
 *
 * Purging is a human's alone ([§6.4](../../../../Specification.md#64-permissions-in-the-mvp)),
 * and this application is only ever a human session — so the control is offered
 * rather than reasoned about here, and an instance that refuses one anyway
 * answers in its own words at the act that asked.
 */
export function PurgeDialog({
  trigger,
  title,
  what,
  confirmLabel = "Purge",
  onConfirm,
}: {
  trigger: ReactElement;
  title: string;
  /** What this particular purge removes, in the words of the thing it removes. */
  what: string;
  confirmLabel?: string;
  onConfirm: () => Promise<void>;
}) {
  return (
    <ActionDialog
      trigger={trigger}
      title={title}
      description={
        <>
          <span className="block">{what}</span>
          <span className="mt-2 block font-medium text-foreground">
            This destroys the undo button. A purge is the second half of a deletion, not a faster
            one, and nothing here brings back what it removes.
          </span>
          <span className="mt-2 block">
            And it does not reach last night's backup. If a value has to be gone everywhere, the
            backups holding it are part of that job, and no button here can finish it.
          </span>
        </>
      }
      confirmLabel={confirmLabel}
      onConfirm={onConfirm}
    />
  );
}
