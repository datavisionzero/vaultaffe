import createClient from "openapi-fetch";
import type { components, paths } from "./schema";
import { dropToken, heldToken } from "@/session/token";

/**
 * The one way this application reaches the instance.
 *
 * The types are generated from `docs/api/openapi.json` before every build
 * (ADR 0006), which is what makes that document load-bearing: a route that
 * changed shape stops compiling here rather than failing on a screen. Nothing
 * hand-writes a URL or a body.
 *
 * The instance it reaches is the one that served this page. In development Vite
 * forwards what belongs to the instance, so that stays true there too.
 */
export const api = createClient<paths>({
  baseUrl: window.location.origin,

  // Reached through `globalThis` when a request is made rather than captured
  // when this module loads, so that a test can stand an instance in front of
  // the generated client rather than in place of it.
  fetch: (request) => globalThis.fetch(request),
});

export type Schemas = components["schemas"];
export type Me = Schemas["Me"];
export type Session = Schemas["Session"];
export type Instance = Schemas["Instance"];
export type Project = Schemas["Project"];
export type Environment = Schemas["Environment"];
export type Secret = Schemas["Secret"];
export type Change = Schemas["Change"];
export type Token = Schemas["Token"];
export type User = Schemas["User"];
export type Invitation = Schemas["Invitation"];
export type InvitationOffer = Schemas["InvitationOffer"];
export type Problem = Schemas["ProblemDetails"];

/**
 * The credential, on every request that has one to send.
 *
 * All three token kinds arrive the same way (`docs/api.md`), so a browser
 * signed in with a session and an agent carrying an agent token are the same
 * request to the instance — which is what "no privileged side door" means in
 * practice. No `Vaultaffe-Client` header goes with it: the contract says a
 * browser sends none, because it is served by the instance it is talking to and
 * cannot be a version behind it.
 */
api.use({
  onRequest({ request }) {
    const token = heldToken();

    if (token !== undefined) {
      request.headers.set("Authorization", `Bearer ${token}`);
    }

    return request;
  },
});

/**
 * The session stopped working somewhere other than the screen the reader is on:
 * it expired, it was revoked, or somebody signed this tab out elsewhere. Every
 * request answers `401` from that moment, and the application has one place to
 * notice rather than one per call.
 *
 * The three endpoints a session is asked about, signed in at and signed out of
 * are the exception: there, `401` is the answer to the question rather than the
 * end of a session.
 */
type SignedOutListener = () => void;

let signedOutListener: SignedOutListener | undefined;

export function whenSignedOut(listener: SignedOutListener): () => void {
  signedOutListener = listener;

  return () => {
    if (signedOutListener === listener) {
      signedOutListener = undefined;
    }
  };
}

const asking = [
  "/api/v1/me",
  "/api/v1/sessions",
  "/api/v1/sessions/current",
  // The two an invited person calls before they are anybody here. A `401` from
  // either is the answer to the question, and there is no session to end.
  "/api/v1/invitations/offer",
  "/api/v1/invitations/acceptance",
];

api.use({
  // Which endpoint answered is read from the request rather than from
  // `response.url`, which is empty on a response nothing fetched — a test
  // instance's, and any hand-built one.
  onResponse({ request, response }) {
    if (response.status === 401 && !asking.includes(new URL(request.url).pathname)) {
      dropToken();
      signedOutListener?.();
    }

    return response;
  },
});

/**
 * The code a client switches on. Unlike the sister project's, it is a field of
 * its own rather than the tail of a URL: `docs/api.md` puts `code` in every
 * problem document precisely so that no client has to parse one.
 */
export function codeOf(problem: Problem | undefined): string | undefined {
  return problem?.code;
}

/** What the generated client answers, whichever endpoint was asked. */
export type Answer<T> = { data?: T; error?: unknown; response: Response };

/**
 * The answer, or the instance's own refusal as something to catch.
 *
 * Every act that changes something reads this way, because the two are one
 * decision: either it happened, or a dialog shows the sentence the instance
 * answered with, at the act that asked (`docs/human-interface.md`).
 */
export async function answered<T>(asking: Promise<Answer<T>>): Promise<T> {
  const { data, error, response } = await asking;

  if (data === undefined) {
    throw new Error(describe(error as Problem | undefined, response.status));
  }

  return data;
}

/**
 * The sentence a refusal is shown as (RFC 9457), or one of ours when the answer
 * was not a problem document at all — the instance is down, or something in
 * between spoke.
 *
 * The instance's own wording is preferred over anything this application could
 * compose, because a refusal names the action and the remedy
 * ([ADR 0010](../../../docs/adr/0010-a-refusal-names-the-action-and-the-client-names-the-command.md))
 * and a screen that paraphrases it loses both.
 */
export function describe(problem: Problem | undefined, status: number): string {
  if (problem?.detail) {
    return problem.detail;
  }

  if (problem?.title) {
    return problem.title;
  }

  return `The instance answered ${status}.`;
}
