import { useState } from "react";
import { answered, api, type MissingKey } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import { WriteDialog } from "./WriteDialog";

const notice = "/api/v1/projects/{project}/environments/{environment}/missing";
const dismissal = "/api/v1/projects/{project}/environments/{environment}/missing/{name}/dismissal";

/**
 * Keys most of this project's other environments have and this one has not
 * ([Specification §6.1](../../../../Specification.md#61-web-ui)).
 *
 * **It displays; it does not act.** Nothing here creates a secret on its own,
 * and there is no button that creates all of them: a suggestion that writes by
 * itself is the wrong convenience in a secrets manager, and the two things this
 * offers are the two a person does — write the key, on the same form as any
 * other, or say "not here" and have it stay said.
 *
 * **The rule is the instance's** and is deliberately not repeated here: a key is
 * missing when more than half of the project's other environments hold it, which
 * is what keeps a personal `dev-alex` from either drowning the notice or
 * silencing it. This screen shows which environments have the key, so that the
 * reader can see the reason rather than trust it.
 *
 * A notice that could not be read says nothing at all. It is an aside on a
 * screen that works without it — a token without `names` for the other
 * environments has no notice rather than a red box where one would be.
 */
export function MissingKeys({
  project,
  environment,
  onChanged,
}: {
  project: string;
  environment: string;
  onChanged: () => void;
}) {
  const [showingDismissed, setShowingDismissed] = useState(false);

  // One question for both halves: the notice comes back whole, each line
  // carrying the moment somebody silenced it or nothing at all
  // ([`api.md`](../../../../docs/api.md)). Splitting it would be a second
  // request on every visit to an environment, for a list this small.
  const [asked, reread] = useAsk<MissingKey[]>(`missing:${project}/${environment}`, () =>
    api.GET(notice, { params: { path: { project, environment } } }),
  );

  const lines = asked.at === "answered" ? asked.data : [];
  const here = lines.filter((key) => key.dismissedAt === null);
  const silenced = lines.filter((key) => key.dismissedAt !== null);

  if (here.length === 0 && silenced.length === 0) {
    return null;
  }

  const showing = showingDismissed ? silenced : here;

  return (
    <section
      aria-label="Keys missing in this environment"
      className="rounded-lg border border-dashed"
    >
      <header className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1 px-3 py-2">
        <h2 className="text-sm font-medium">
          {showingDismissed
            ? `Dismissed in ${environment}`
            : `Missing in ${environment}, present elsewhere`}
        </h2>
        {silenced.length > 0 && (
          <button
            type="button"
            className="text-xs text-muted-foreground underline-offset-2 hover:underline"
            onClick={() => setShowingDismissed((was) => !was)}
          >
            {showingDismissed
              ? `Back to the ${here.length} missing`
              : `${silenced.length} dismissed`}
          </button>
        )}
      </header>

      {showing.length === 0 ? (
        <p className="px-3 pb-3 text-sm text-muted-foreground">
          {showingDismissed ? "Nothing is dismissed here." : "Nothing is missing here."}
        </p>
      ) : (
        <ul className="divide-y border-t">
          {showing.map((key) => (
            <li
              key={key.name}
              className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2.5"
            >
              <div className="min-w-0 flex-1">
                <p className="font-mono text-sm break-all">{key.name}</p>
                <p className="text-xs text-muted-foreground">
                  in {key.presentIn.join(", ")}
                </p>
              </div>

              {showingDismissed ? (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    void answered(
                      api.DELETE(dismissal, {
                        params: { path: { project, environment, name: key.name } },
                      }),
                    ).then(reread);
                  }}
                >
                  Mention again
                </Button>
              ) : (
                <div className="flex items-center gap-2">
                  <WriteDialog
                    project={project}
                    environment={environment}
                    name={key.name}
                    trigger={
                      <Button variant="outline" size="sm">
                        Add it here
                      </Button>
                    }
                    onWritten={() => {
                      reread();
                      onChanged();
                    }}
                  />
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                      void answered(
                        api.POST(dismissal, {
                          params: { path: { project, environment, name: key.name } },
                        }),
                      ).then(reread);
                    }}
                  >
                    Not here
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
