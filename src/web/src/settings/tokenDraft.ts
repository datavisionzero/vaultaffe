import type { Project, Token } from "@/api/client";

/**
 * What a token is described by, as the two screens that describe one pass it
 * about: the scopes, the reach, and the draft a form holds while somebody is
 * deciding.
 *
 * It is beside `TokenFields.tsx` rather than in it because these are values and
 * that is a component — a rule the linter keeps, and a distinction worth keeping
 * anyway: `/settings/tokens` and `/enroll` share what a token *is*, and each
 * draws its own screen.
 */

/** The four scopes, in the order the specification declares them. */
export const scopes = ["names", "read", "write", "delete"] as const;

export type Scope = (typeof scopes)[number];

/** The environments a token bound to nothing but production would not reach. */
export const production = ["prod", "production"];

/** One project, or one environment of it, that a token may touch. */
export type Bound = { projectId: string; environmentId: string | null };

/**
 * A binding as one string, so that the picker is a set of checkboxes rather than
 * a list to search through. A project with nothing after the colon is the whole
 * of it — which is the binding the CLI writes with `--project` alone, and the one
 * this screen could not show before it could also change one.
 */
export function keyOf(binding: Bound): string {
  return `${binding.projectId}:${binding.environmentId ?? ""}`;
}

export function boundBy(key: string): Bound {
  const [projectId, environmentId] = key.split(":");

  return { projectId, environmentId: environmentId === "" ? null : environmentId };
}

export type Reach = "organization" | "not-production" | "picked";

/**
 * What a person is filling in, in both dialogs. Creating adds a kind to it and
 * changing does not, which is the only difference between the two forms — and
 * the reason they are one component: a scope offered when a token is issued and
 * withheld when it is amended would be a permission model nobody wrote down.
 */
export type Draft = { name: string; scopes: Scope[]; reach: Reach; picked: string[] };

/** Every environment not called production, as bindings. */
export function outsideProduction(catalogue: Project[]): Bound[] {
  return catalogue.flatMap((project) =>
    project.environments
      .filter((environment) => !production.includes(environment.name))
      .map((environment) => ({ projectId: project.id, environmentId: environment.id })),
  );
}

/** What the draft's reach means as the bindings the instance is sent. */
export function bindingsOf(catalogue: Project[], draft: Draft): Bound[] {
  if (draft.reach === "organization") {
    return [];
  }

  if (draft.reach === "not-production") {
    return outsideProduction(catalogue);
  }

  return draft.picked.map(boundBy);
}

/**
 * The draft a token that already exists starts from, so that the dialog opens on
 * what is true rather than on a blank form somebody has to reconstruct.
 *
 * A bound token opens on "only what I pick" and never on "everything except
 * production": the two can describe the same set today and stop agreeing the
 * moment an environment is added, and a form that guessed would silently widen a
 * binding the next time somebody saved it.
 */
export function draftOf(token: Token): Draft {
  return {
    name: token.name ?? "",
    scopes: [...scopes].filter((scope) => token.scopes.includes(scope)),
    reach: token.reachesTheWholeOrganization ? "organization" : "picked",
    picked: token.bindings.map(keyOf),
  };
}
