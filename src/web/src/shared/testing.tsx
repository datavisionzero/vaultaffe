import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { MemoryRouter } from "react-router";
import { vi } from "vitest";
import type { Me } from "@/api/client";
import { ThemeProvider } from "@/components/theme-provider";
import { TooltipProvider } from "@/components/ui/tooltip";
import { SessionProvider } from "@/session/Session";
import { dropToken, holdToken } from "@/session/token";

/**
 * An instance to stand in front of the generated client: a route table of
 * `METHOD /path` to what it answers. Anything not listed answers 404 with a
 * problem document, the way the real one would.
 */
export type Answer = { status?: number; body?: unknown } | Record<string, unknown> | unknown[];
export type Route = Answer | ((request: Request) => Answer);

export function installInstance(routes: Record<string, Route>) {
  const calls: Request[] = [];

  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(input, init);
    calls.push(request);

    const url = new URL(request.url);
    const route = `${request.method} ${url.pathname}`;
    const answer = routes[route];

    if (answer === undefined) {
      return problem(404, `no route ${route}`);
    }

    const resolved = typeof answer === "function" ? answer(request) : answer;
    const { status, body } = isEnvelope(resolved) ? resolved : { status: 200, body: resolved };

    return new Response(body === undefined ? null : JSON.stringify(body), {
      status: status ?? 200,
      headers: { "content-type": "application/json" },
    });
  });

  vi.stubGlobal("fetch", fetch);

  return { calls, fetch };
}

function isEnvelope(value: unknown): value is { status?: number; body?: unknown } {
  return typeof value === "object" && value !== null && ("status" in value || "body" in value) && !("name" in value);
}

/** A refusal, in the shape `docs/api.md` fixes: RFC 9457 plus the `code`. */
export function problem(status: number, code: string, detail = "Refused."): Response {
  return new Response(JSON.stringify({ type: `/problems/${code}`, title: code, status, detail, code }), {
    status,
    headers: { "content-type": "application/problem+json" },
  });
}

/** A tab that is signed in, and one that is not. */
export function signedIn(token = "vaultaffe_session_testtoken") {
  holdToken(token);
  return () => dropToken();
}

export function renderAt(path: string, element: ReactElement) {
  return render(
    <ThemeProvider storageKey="test.theme">
      <TooltipProvider>
        <MemoryRouter initialEntries={[path]}>{element}</MemoryRouter>
      </TooltipProvider>
    </ThemeProvider>,
  );
}

/** The same, with somebody signed in: what every screen under the shell has. */
export function renderUnderShell(path: string, element: ReactElement, me: Me = aPerson) {
  return renderAt(
    path,
    <SessionProvider value={{ me, signOut: () => undefined }}>{element}</SessionProvider>,
  );
}

export const aPerson: Me = {
  organizationId: "0199a000-0000-7000-8000-000000000001",
  userId: "0199a000-0000-7000-8000-000000000002",
  name: "maintainer",
  isAdministrator: true,
  tokenId: "0199a000-0000-7000-8000-000000000003",
  tokenName: null,
  tokenKind: "session",
  scopes: ["names", "read", "write", "delete"],
};
