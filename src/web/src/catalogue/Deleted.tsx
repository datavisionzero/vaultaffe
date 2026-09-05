import { useId } from "react";

/**
 * The switch for what is deleted and recoverable, at all three levels
 * (`docs/human-interface.md`).
 *
 * It is a switch rather than a second screen because what is deleted is the same
 * list seen from 72 hours ago: `?deleted=true` on the same endpoint answers it
 * ([`api.md`](../../../../docs/api.md)). The label says "recoverable" and not
 * "deleted" alone, because the window is the point — after it, the row is gone
 * and there is nothing here to show.
 */
export function Deleted({
  showing,
  onShowing,
}: {
  showing: boolean;
  onShowing: (showing: boolean) => void;
}) {
  const id = useId();

  return (
    <label htmlFor={id} className="flex items-center gap-1.5 text-xs text-muted-foreground">
      <input
        id={id}
        type="checkbox"
        className="size-3.5 accent-primary"
        checked={showing}
        onChange={(event) => onShowing(event.target.checked)}
      />
      Deleted and recoverable
    </label>
  );
}
