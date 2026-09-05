import type { ReactNode } from "react";

/**
 * The frame of the screens outside the shell: a centred single column, which is
 * what `docs/human-interface.md` gives the first run, signing in, confirming a
 * device login, and accepting an invitation.
 *
 * They have no sidebar and no account menu because there is no session behind
 * them yet — that is the whole difference between these screens and every other
 * one, and it is a frame rather than a flag.
 */
export function Doorstep({
  title,
  meta,
  children,
}: {
  title: string;
  meta?: string;
  children: ReactNode;
}) {
  return (
    <main className="flex min-h-svh flex-col items-center justify-center p-6">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex items-center gap-2 text-base font-semibold">
          <span aria-hidden className="size-4.5 rounded-sm bg-brand" />
          vaultaffe
        </div>
        <h1 className="text-lg font-semibold">{title}</h1>
        {meta !== undefined && <p className="mb-5 text-sm text-muted-foreground">{meta}</p>}
        <div className={meta === undefined ? "mt-5" : undefined}>{children}</div>
      </div>
    </main>
  );
}
