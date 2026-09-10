import type { Project } from "@/api/client";
import { Field } from "@/shared/Form";
import { boundBy, keyOf, outsideProduction, scopes, type Draft, type Scope } from "./tokenDraft";

/**
 * The form a token is described in, and the two screens that fill it in.
 *
 * `/settings/tokens` fills it in to create one or to change one; `/enroll` fills
 * it in when a person decides what a machine that asked for a token may do
 * ([ADR 0021](../../../../docs/adr/0021-an-agent-asks-for-its-own-token.md)).
 * One component rather than three, because three would drift: a scope offered
 * when a token is issued and withheld when it is agreed to would be a permission
 * model nobody wrote down.
 */

/**
 * What each scope is, said where a person is choosing one rather than in a
 * document they would have to go and find.
 */
const whatScopeIs: Record<Scope, string> = {
  names: "See which keys exist and which are still empty. Never a value.",
  read: "Read one value at a time.",
  write: "Write values, create keys and placeholders, roll one back.",
  delete: "Delete a secret, an environment or a project — recoverably — and restore one.",
};

/**
 * The three things a person decides about a token, in both dialogs: what it is
 * called, what it may do, and how far it reaches.
 */
export function TokenFields({
  catalogue,
  draft,
  change,
  hint,
}: {
  catalogue: Project[];
  draft: Draft;
  change: (part: Partial<Draft>) => void;
  hint: string;
}) {
  const away = outsideProduction(catalogue);

  return (
    <>
      <Field
        label="Name"
        required
        maxLength={100}
        value={draft.name}
        onChange={(event) => change({ name: event.target.value })}
        hint={hint}
      />

      <fieldset className="grid gap-2">
        <legend className="text-sm font-medium">Scopes</legend>
        {scopes.map((scope) => (
          <label key={scope} className="flex items-start gap-2 text-sm">
            <input
              type="checkbox"
              name={`scope-${scope}`}
              className="mt-0.5 size-4 accent-primary"
              checked={draft.scopes.includes(scope)}
              onChange={(event) =>
                change({
                  scopes: event.target.checked
                    ? [...scopes].filter((one) => draft.scopes.includes(one) || one === scope)
                    : draft.scopes.filter((one) => one !== scope),
                })
              }
            />
            <span>
              <span className="font-mono">{scope}</span>
              <span className="block text-xs text-muted-foreground">{whatScopeIs[scope]}</span>
            </span>
          </label>
        ))}
      </fieldset>

      <fieldset className="grid gap-2">
        <legend className="text-sm font-medium">What it reaches</legend>
        <Choice
          name="reach"
          checked={draft.reach === "organization"}
          onChange={() => change({ reach: "organization" })}
          label="The whole organization"
          what="The default for an agent token: the point is attribution, not restriction."
        />
        <Choice
          name="reach"
          checked={draft.reach === "not-production"}
          onChange={() => change({ reach: "not-production" })}
          label="Everything except production"
          what={
            away.length === 0
              ? "There is nothing outside production to bind to yet."
              : `Binds it to ${away.length} environments — every one not called prod or production.`
          }
        />
        <Choice
          name="reach"
          checked={draft.reach === "picked"}
          onChange={() => change({ reach: "picked" })}
          label="Only what I pick"
          what="Whole projects, single environments, or any mixture of them."
        />

        {draft.reach === "picked" && (
          <div className="grid gap-2 rounded-lg border p-2">
            {catalogue.length === 0 && (
              <p className="text-xs text-muted-foreground">There are no projects to bind to yet.</p>
            )}
            {catalogue.map((project) => {
              // The whole project is one binding and not a shorthand for its
              // environments: it keeps covering one added tomorrow, which is
              // usually what somebody adding a project to a token means. So it
              // takes the individual ones with it rather than sitting beside
              // them and leaving two answers on the screen.
              const whole = draft.picked.includes(keyOf({ projectId: project.id, environmentId: null }));

              return (
                <div key={project.id}>
                  <label className="flex items-center gap-1.5">
                    <input
                      type="checkbox"
                      name={`project-${project.id}`}
                      className="size-3.5 accent-primary"
                      checked={whole}
                      onChange={(event) =>
                        change({
                          picked: event.target.checked
                            ? [
                                ...draft.picked.filter(
                                  (one) => boundBy(one).projectId !== project.id,
                                ),
                                keyOf({ projectId: project.id, environmentId: null }),
                              ]
                            : draft.picked.filter(
                                (one) => one !== keyOf({ projectId: project.id, environmentId: null }),
                              ),
                        })
                      }
                    />
                    <span className="font-mono text-xs">{project.name}</span>
                    <span className="text-xs text-muted-foreground">every environment</span>
                  </label>
                  <div className="flex flex-wrap gap-x-4 pl-5">
                    {project.environments.map((environment) => {
                      const key = keyOf({
                        projectId: project.id,
                        environmentId: environment.id,
                      });

                      return (
                        <label key={environment.id} className="flex items-center gap-1.5 text-sm">
                          <input
                            type="checkbox"
                            name={`environment-${environment.id}`}
                            className="size-3.5 accent-primary"
                            disabled={whole}
                            checked={whole || draft.picked.includes(key)}
                            onChange={(event) =>
                              change({
                                picked: event.target.checked
                                  ? [...draft.picked, key]
                                  : draft.picked.filter((one) => one !== key),
                              })
                            }
                          />
                          <span className="font-mono text-xs">{environment.name}</span>
                        </label>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </fieldset>
    </>
  );
}

/** One of a set of choices, with what it means under it rather than beside it. */
export function Choice({
  name,
  checked,
  onChange,
  label,
  what,
}: {
  name: string;
  checked: boolean;
  onChange: () => void;
  label: string;
  what: string;
}) {
  return (
    <label className="flex items-start gap-2 text-sm">
      <input
        type="radio"
        name={name}
        className="mt-0.5 size-4 accent-primary"
        checked={checked}
        onChange={onChange}
      />
      <span>
        {label}
        <span className="block text-xs text-muted-foreground">{what}</span>
      </span>
    </label>
  );
}
