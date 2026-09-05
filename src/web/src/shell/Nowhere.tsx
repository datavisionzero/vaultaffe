import { PageHeader } from "@/shared/PageHeader";

/**
 * An address this application has no screen for. Not an error of the instance.
 *
 * Its neighbour used to live here too: a screen the matrix named and the frame
 * did not have yet, which said so plainly rather than pretending. There is none
 * left — every route of `docs/human-interface.md` leads to the screen it names —
 * so what remains is the honest answer to an address that names nothing.
 */
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
