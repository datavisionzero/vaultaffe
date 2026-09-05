import { FilterIcon } from "lucide-react";
import { useState } from "react";
import { useSearchParams } from "react-router";
import { api, type ChangePage } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { Field, Refusal } from "@/shared/Form";
import { PageHeader } from "@/shared/PageHeader";
import { Rows } from "@/shared/Rows";
import { Entries } from "./Entries";

/** What one page of the log holds. The contract allows up to 200. */
const perPage = 50;

/** The three names the instance narrows the log by, widest first. */
type Filter = { project: string; environment: string; secret: string };

/**
 * `/changes` — what was changed, by whom and **by what kind of thing**
 * (`docs/human-interface.md`).
 *
 * **The filter is in the address.** A reader who found the three entries that
 * explain an incident can hand that page to somebody, and the back button walks
 * out of a filter the way it walks out of anything else. It is also the only
 * honest place for it: the log is a listing the instance narrows, not one this
 * screen sifts, and the endpoint takes names rather than ids so that a binding
 * cannot quietly widen or narrow the answer afterwards
 * ([`api.md`](../../../../docs/api.md)).
 *
 * **A narrower filter needs the wider one.** `environment` without `project`
 * would be a search across every project's `prod`, which is not what anybody
 * means, and the instance refuses it rather than guessing. This screen never
 * asks for it: the field below stays closed until the one above it says
 * something.
 */
export function Changes() {
  const [params, setParams] = useSearchParams();
  const [filtering, setFiltering] = useState(false);

  const filter = {
    project: params.get("project")?.trim() ?? "",
    environment: params.get("environment")?.trim() ?? "",
    secret: params.get("secret")?.trim() ?? "",
  };

  // An address is somebody's to type, so what comes out of it is read rather
  // than trusted: a negative or unreadable offset is the first page.
  const offset = Math.max(0, Math.trunc(Number(params.get("offset") ?? "0")) || 0);
  const filtered = filter.project !== "" || filter.environment !== "" || filter.secret !== "";

  const [asked] = useAsk<ChangePage>(
    `changes:${filter.project}/${filter.environment}/${filter.secret}:${offset}`,
    () =>
      api.GET("/api/v1/changes", {
        params: {
          query: {
            // What the address says, and nothing it does not: an absent filter
            // is an absent parameter rather than an empty one.
            ...(filter.project !== "" && { project: filter.project }),
            ...(filter.environment !== "" && { environment: filter.environment }),
            ...(filter.secret !== "" && { secret: filter.secret }),
            limit: perPage,
            offset,
          },
        },
      }),
  );

  /** The address, which is where both the filter and the page live. */
  function address(next: Filter, page = 0) {
    setParams(
      Object.fromEntries(
        Object.entries({ ...next, ...(page > 0 && { offset: String(page) }) }).filter(
          ([, value]) => value !== "",
        ),
      ),
    );
  }

  function show(next: Filter) {
    // A different filter is a different log, so it starts at the top of that one
    // rather than at the offset the last one happened to be at.
    address(next);
    setFiltering(false);
  }

  return (
    <>
      <PageHeader title="Change log" meta={filtered ? "filtered" : undefined}>
        <Button variant="outline" size="sm" className="md:hidden" onClick={() => setFiltering(true)}>
          <FilterIcon />
          Filters
        </Button>
      </PageHeader>

      <div className="flex flex-1 flex-col gap-4 p-4">
        <div className="hidden md:block">
          <Filters key={`${filter.project}/${filter.environment}/${filter.secret}`} filter={filter} onFilter={show} />
        </div>

        {asked.at === "asking" && <Rows count={6} />}
        {asked.at === "refused" && <Refusal>{asked.why}</Refusal>}
        {asked.at === "answered" &&
          (asked.data.entries.length === 0 ? (
            <p className="rounded-lg border border-dashed px-3 py-10 text-center text-sm text-muted-foreground">
              {filtered
                ? "Nothing in the log matches this filter."
                : "Nothing has been changed in this organization yet."}
            </p>
          ) : (
            <>
              <Entries entries={asked.data.entries} />
              <Paging
                offset={offset}
                shown={asked.data.entries.length}
                total={asked.data.total}
                onPage={(page) => address(filter, page)}
              />
            </>
          ))}

        {/* The promise this product deliberately does not make. Reads are not
            recorded, revealing included, and a screen that stayed quiet about
            it would let a reader assume the opposite
            (Specification §6.5). */}
        <p className="max-w-prose text-xs text-muted-foreground">
          The log holds what was changed and never what it was changed to — no value appears here,
          not even as a diff. Reads are not in it at all: revealing a value, exporting an
          environment and <span className="font-mono">run</span> reading one are not recorded.
        </p>
      </div>

      <Sheet open={filtering} onOpenChange={setFiltering}>
        <SheetContent side="bottom">
          <SheetHeader>
            <SheetTitle>Filter the log</SheetTitle>
            <SheetDescription>
              A narrower filter needs the wider one: an environment is named inside a project, and a
              key inside an environment.
            </SheetDescription>
          </SheetHeader>
          <div className="p-4 pt-0">
            <Filters key={`sheet:${filter.project}/${filter.environment}/${filter.secret}`} filter={filter} onFilter={show} />
          </div>
        </SheetContent>
      </Sheet>
    </>
  );
}

/**
 * The three names the instance narrows by, and the rule between them.
 *
 * It is a form and applies on submit rather than on every keystroke: a filter is
 * a question asked once, and a request per letter would page a log that is
 * being appended to underneath. Clearing a wider field clears the narrower ones
 * with it, which is what keeps the invalid combination unreachable from here
 * rather than merely refused.
 */
function Filters({ filter, onFilter }: { filter: Filter; onFilter: (filter: Filter) => void }) {
  const [draft, setDraft] = useState(filter);

  function change(next: Partial<Filter>) {
    const merged = { ...draft, ...next };

    setDraft({
      project: merged.project,
      environment: merged.project === "" ? "" : merged.environment,
      secret: merged.project === "" || merged.environment === "" ? "" : merged.secret,
    });
  }

  return (
    <form
      className="grid gap-3 rounded-lg border p-3 sm:grid-cols-3"
      onSubmit={(event) => {
        event.preventDefault();
        onFilter(draft);
      }}
    >
      <Field
        label="Project"
        value={draft.project}
        onChange={(event) => change({ project: event.target.value.toLowerCase() })}
        hint="Leave it empty for the whole organization."
      />
      <Field
        label="Environment"
        value={draft.environment}
        disabled={draft.project === ""}
        onChange={(event) => change({ environment: event.target.value.toLowerCase() })}
        hint={draft.project === "" ? "Name a project first." : "One environment of that project."}
      />
      <Field
        label="Key"
        value={draft.secret}
        disabled={draft.environment === ""}
        onChange={(event) => change({ secret: event.target.value.toUpperCase() })}
        className="font-mono"
        hint={draft.environment === "" ? "Name an environment first." : "One key of that environment."}
      />
      <div className="flex items-center gap-2 sm:col-span-3">
        <Button type="submit" size="sm">
          Filter
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => onFilter({ project: "", environment: "", secret: "" })}
        >
          Clear
        </Button>
      </div>
    </form>
  );
}

/**
 * Where this page sits in the log, and the way to the next one.
 *
 * The log is appended to while somebody reads it, so entries arriving during a
 * page shift what the next one holds. That is what an append-only log does, and
 * it is said here rather than hidden behind a cursor this product does not
 * need ([`api.md`](../../../../docs/api.md)).
 */
function Paging({
  offset,
  shown,
  total,
  onPage,
}: {
  offset: number;
  shown: number;
  total: number;
  onPage: (offset: number) => void;
}) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <p role="status" className="text-xs text-muted-foreground">
        {offset + 1}–{offset + shown} of {total}, newest first
      </p>
      <div className="ml-auto flex gap-2">
        <Button
          variant="outline"
          size="sm"
          disabled={offset === 0}
          onClick={() => onPage(Math.max(0, offset - perPage))}
        >
          Newer
        </Button>
        <Button
          variant="outline"
          size="sm"
          disabled={offset + shown >= total}
          onClick={() => onPage(offset + perPage)}
        >
          Older
        </Button>
      </div>
    </div>
  );
}
