import { useId, type ComponentProps, type ReactNode } from "react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

/**
 * A labelled field, and the two things that stand at it: what it is for, and
 * what the instance said about it.
 *
 * `docs/human-interface.md`: a refusal that names a field stands **at that
 * field**, never at the foot of the form — which is only true if every field is
 * built somewhere that knows where to put one.
 */
type FieldProps = ComponentProps<typeof Input> & {
  label: ReactNode;
  /** What this field is for, when the label alone does not say it. */
  hint?: ReactNode;
  /** The instance's own sentence about this field. */
  problem?: string;
};

export function Field({ label, hint, problem, className, ...props }: FieldProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const problemId = `${id}-problem`;

  return (
    <div className="grid gap-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>
      <Input
        id={id}
        aria-invalid={problem === undefined ? undefined : true}
        aria-describedby={cn(hint !== undefined && hintId, problem !== undefined && problemId) || undefined}
        className={className}
        {...props}
      />
      {hint !== undefined && (
        <p id={hintId} className="text-xs text-muted-foreground">
          {hint}
        </p>
      )}
      {problem !== undefined && (
        <p id={problemId} role="alert" className="text-xs text-destructive">
          {problem}
        </p>
      )}
    </div>
  );
}

/**
 * What the instance answered, at the act that asked for it.
 *
 * It is the instance's own wording rather than anything this application could
 * compose: a refusal names the action and the remedy
 * ([ADR 0010](../../../docs/adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md)),
 * and a screen that paraphrases one loses both.
 */
export function Refusal({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <p role="alert" className={cn("text-sm text-destructive", className)}>
      {children}
    </p>
  );
}

/** The counterpart: what an act did, said where the act was asked for. */
export function Done({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <p role="status" className={cn("text-sm text-muted-foreground", className)}>
      {children}
    </p>
  );
}
