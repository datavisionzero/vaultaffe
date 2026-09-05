import { screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderAt, signedIn } from "@/shared/testing";
import { heldToken } from "@/session/token";
import { App } from "./App";

afterEach(() => {
  vi.unstubAllGlobals();
});

/** What the instance answers a request carrying a token it does not admit. */
const refused = {
  status: 401,
  body: { type: "/problems/unauthenticated", title: "unauthenticated", status: 401, code: "unauthenticated" },
};

describe("the first paint", () => {
  // `docs/human-interface.md`: loading is a designed state, not a blank page.
  it("says what it is waiting for while it asks who this tab is", async () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => undefined)));

    renderAt("/", <App />);

    expect(await screen.findByRole("status")).toHaveTextContent("Checking your session");
    expect(screen.getByRole("main")).toHaveAttribute("aria-busy");
  });

  it("sends the token of this tab, and nothing else identifying", async () => {
    const forget = signedIn("vaultaffe_session_abc");
    const { calls } = installInstance({ "GET /api/v1/me": refused });

    renderAt("/", <App />);
    await screen.findByText("The sign-in screen is not built yet.");

    expect(calls[0]!.headers.get("Authorization")).toBe("Bearer vaultaffe_session_abc");
    // The contract says a browser sends no client version: it is served by the
    // instance it talks to and cannot be a release behind it (`docs/api.md`).
    expect(calls[0]!.headers.get("Vaultaffe-Client")).toBeNull();
    forget();
  });

  // A token that does not authenticate is one of four things, and which one is
  // information about a credential the caller does not hold (`docs/api.md`).
  it("forgets a token the instance would not admit", async () => {
    const forget = signedIn("vaultaffe_session_stale");
    installInstance({ "GET /api/v1/me": refused });

    renderAt("/", <App />);
    await screen.findByText("The sign-in screen is not built yet.");

    expect(heldToken()).toBeUndefined();
    forget();
  });

  it("offers the way back when the instance does not answer at all", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new Error("no route to host"))));

    renderAt("/", <App />);

    expect(await screen.findByText("The instance did not answer.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });
});
