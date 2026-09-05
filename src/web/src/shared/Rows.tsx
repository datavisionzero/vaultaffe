import { Skeleton } from "@/components/ui/skeleton";

/**
 * A list on its way: a skeleton of rows inside the frame that is already on the
 * screen, never a spinner in the middle of a page
 * (`docs/human-interface.md`, the loading state).
 */
export function Rows({ count = 3 }: { count?: number }) {
  return (
    <ul aria-busy className="divide-y rounded-lg border">
      {Array.from({ length: count }, (_, row) => (
        <li key={row} className="px-3 py-3">
          <Skeleton className="h-4 w-40" />
        </li>
      ))}
    </ul>
  );
}
