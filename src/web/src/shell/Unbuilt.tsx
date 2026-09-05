import { PageHeader } from "@/shared/PageHeader";

/**
 * A screen the matrix names and the frame does not have yet.
 *
 * `docs/human-interface.md` fixes every screen of this application before any
 * of them is built, and the frame is built before the screens are. So the route
 * exists, the navigation leads to it, and what stands there says plainly that
 * it is not built — which is the one thing a scaffold must not lie about. Each
 * of these is replaced whole by the screen it names; none of them grows into
 * one.
 */
export function Unbuilt({ screen, what }: { screen: string; what: string }) {
  return (
    <>
      <PageHeader title={screen} />
      <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
        <p className="font-medium">This screen is not built yet.</p>
        <p className="max-w-prose text-sm text-muted-foreground">{what}</p>
      </div>
    </>
  );
}

/** An address this application has no screen for. Not an error of the instance. */
export function Nowhere() {
  return (
    <>
      <PageHeader title="Not a page here" />
      <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
        <p className="font-medium">There is nothing at this address.</p>
        <p className="text-sm text-muted-foreground">
          The navigation on the left leads everywhere this application goes.
        </p>
      </div>
    </>
  );
}
