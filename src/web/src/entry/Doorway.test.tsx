import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderAt } from "@/shared/testing";
import { Doorway } from "./Doorway";

afterEach(() => {
  vi.unstubAllGlobals();
});

const started = { started: true, organizationName: "Default" };

const session = {
  userId: "0199a000-0000-7000-8000-000000000002",
  name: "Maintainer",
  email: "maintainer@example.test",
  isAdministrator: true,
  token: "vaultaffe_session_thenewone",
  expiresAt: "2026-10-05T18:00:00+00:00",
};

describe("signing in", () => {
  it("stands at whatever address a stranger asked for, so signing in lands there", async () => {
    installInstance({ "GET /api/v1/instance": started });

    renderAt("/projects/landing-page/prod", <Doorway onSignedIn={() => undefined} />);

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });

  it("hands the token over and says whose instance this is", async () => {
    const { calls } = installInstance({
      "GET /api/v1/instance": started,
      "POST /api/v1/sessions": session,
    });

    const held: string[] = [];
    renderAt("/login", <Doorway onSignedIn={(token) => held.push(token)} />);

    expect(await screen.findByText("to Default")).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText("Email"), "maintainer@example.test");
    await userEvent.type(screen.getByLabelText("Password"), "a-password-of-real-length");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(held).toEqual(["vaultaffe_session_thenewone"]);

    // The token is what the answer carried; nothing about the password is kept.
    const signIn = calls.find((call) => new URL(call.url).pathname === "/api/v1/sessions")!;
    expect(signIn.method).toBe("POST");
  });

  it("shows the instance's own sentence when it refuses, at the act that asked", async () => {
    installInstance({
      "GET /api/v1/instance": started,
      "POST /api/v1/sessions": {
        status: 401,
        body: {
          type: "/problems/unauthenticated",
          title: "unauthenticated",
          status: 401,
          code: "unauthenticated",
          detail: "That email address and password do not match.",
        },
      },
    });

    renderAt("/login", <Doorway onSignedIn={() => undefined} />);

    await userEvent.type(await screen.findByLabelText("Email"), "somebody@example.test");
    await userEvent.type(screen.getByLabelText("Password"), "not-the-right-password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "That email address and password do not match.",
    );
  });

  // A reader in front of a fresh installation should read that, rather than
  // conclude that they typed their password wrongly.
  it("says when there is no instance to sign in to yet", async () => {
    installInstance({ "GET /api/v1/instance": { started: false, organizationName: null } });

    renderAt("/login", <Doorway onSignedIn={() => undefined} />);

    expect(await screen.findByText(/has not been started yet/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Password")).not.toBeInTheDocument();
  });

  it("says who performs a reset, because nothing here sends an email", async () => {
    installInstance({ "GET /api/v1/instance": started });

    renderAt("/login", <Doorway onSignedIn={() => undefined} />);

    expect(await screen.findByText(/reset is done by an administrator/)).toBeInTheDocument();
  });
});

describe("an invitation", () => {
  const offer = {
    organizationName: "Default",
    email: "newcomer@example.test",
    name: "Newcomer",
    state: "open",
  };

  it("reads the code out of the fragment and never puts it in a URL", async () => {
    const { calls } = installInstance({
      "POST /api/v1/invitations/offer": offer,
      "POST /api/v1/invitations/acceptance": session,
    });

    renderAt("/invite#the-code-itself", <Doorway onSignedIn={() => undefined} />);

    expect(await screen.findByRole("heading", { name: "Join" })).toBeInTheDocument();

    const asked = calls.find((call) => call.url.includes("/invitations/offer"))!;
    expect(new URL(asked.url).search).toBe("");
    expect(asked.url).not.toContain("the-code-itself");
    expect(await asked.clone().json()).toEqual({ code: "the-code-itself" });
  });

  it("takes a password, and lands the new person inside rather than at a sign-in", async () => {
    installInstance({
      "POST /api/v1/invitations/offer": offer,
      "POST /api/v1/invitations/acceptance": session,
    });

    const held: string[] = [];
    renderAt("/invite#the-code-itself", <Doorway onSignedIn={(token) => held.push(token)} />);

    // The address is the invitation's: it is shown and cannot be typed over.
    expect(await screen.findByLabelText("Email")).toBeDisabled();

    await userEvent.type(screen.getByLabelText("Password"), "a-password-of-real-length");
    await userEvent.click(screen.getByRole("button", { name: "Join the organization" }));

    expect(held).toEqual(["vaultaffe_session_thenewone"]);
  });

  it("says which of the three ways a link stopped working", async () => {
    installInstance({ "POST /api/v1/invitations/offer": { ...offer, state: "accepted" } });

    renderAt("/invite#the-code-itself", <Doorway onSignedIn={() => undefined} />);

    expect(await screen.findByText(/already been used/)).toBeInTheDocument();
  });

  it("says so when the link is only half of one", async () => {
    installInstance({});

    renderAt("/invite", <Doorway onSignedIn={() => undefined} />);

    expect(await screen.findByRole("heading", { name: "This link leads nowhere" })).toBeInTheDocument();
  });
});
