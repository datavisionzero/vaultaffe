import { CheckIcon, CopyIcon } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";

/**
 * A value a reader is meant to take with them, and the control that takes it.
 *
 * Used for the two things this product hands over exactly once — an invitation
 * link and a token value — and for a revealed secret. The clipboard is better
 * than the screen for the shoulder behind the reader and identical for
 * everything else, which is why `docs/human-interface.md` treats copying and
 * revealing as the same act and asks that the control say which it is.
 */
export function Copyable({
  value,
  label,
  hidden = false,
}: {
  value: string;
  label: string;
  /** Kept off the screen: the reader wanted it on the clipboard, not in the room. */
  hidden?: boolean;
}) {
  const [copied, setCopied] = useState(false);

  return (
    <div className="flex items-center gap-2">
      {!hidden && (
        <code className="min-w-0 flex-1 truncate rounded-md border bg-muted/50 px-2 py-1.5 font-mono text-xs">
          {value}
        </code>
      )}
      <Button
        variant="outline"
        size="sm"
        aria-label={`Copy the ${label.toLowerCase()}`}
        onClick={() => {
          void navigator.clipboard.writeText(value).then(() => {
            setCopied(true);
            window.setTimeout(() => setCopied(false), 2000);
          });
        }}
      >
        {copied ? <CheckIcon /> : <CopyIcon />}
        {copied ? "Copied" : "Copy"}
      </Button>
    </div>
  );
}
