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

  it("says who performs a reset, because nothing here sends an email", async () => {
    installInstance({ "GET /api/v1/instance": started });

    renderAt("/login", <Doorway onSignedIn={() => undefined} />);

    expect(await screen.findByText(/reset is done by an administrator/)).toBeInTheDocument();
  });
});

describe("the first run", () => {
  const unstarted = { started: false, organizationName: null, needsClaim: true };

  // What the operator pastes out of the instance's own log (ADR 0019).
  const claimSecret = "vaultaffe_claim_" + "a".repeat(43);

  // A reader in front of a fresh installation gets the one screen it has,
  // rather than a sign-in form that would read as a password gone wrong.
  it("is the only screen an unstarted instance has, at whatever address was asked for", async () => {
    installInstance({ "GET /api/v1/instance": unstarted });

    renderAt("/projects/landing-page/prod", <Doorway onSignedIn={() => undefined} />);

    expect(await screen.findByRole("heading", { name: "Start this instance" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sign in" })).not.toBeInTheDocument();
  });

  it("lands the first administrator inside, rather than at a sign-in", async () => {
    const { calls } = installInstance({
      "GET /api/v1/instance": unstarted,
      "POST /api/v1/instance": {
        organizationId: "0199a000-0000-7000-8000-000000000001",
        organizationName: "Default",
        session,
      },
    });

    const held: string[] = [];
    renderAt("/start", <Doorway onSignedIn={(token) => held.push(token)} />);

    await userEvent.type(await screen.findByLabelText("Your name"), "Maintainer");
    await userEvent.type(screen.getByLabelText("Email"), "maintainer@example.test");
    await userEvent.type(screen.getByLabelText("Password"), "a-password-of-real-length");
    await userEvent.type(screen.getByLabelText("Claim secret"), claimSecret);
    await userEvent.click(screen.getByRole("button", { name: "Start the instance" }));

    expect(held).toEqual(["vaultaffe_session_thenewone"]);

    const firstRun = calls.find(
      (call) => call.method === "POST" && new URL(call.url).pathname === "/api/v1/instance",
    )!;
    expect(await firstRun.clone().json()).toEqual({
      email: "maintainer@example.test",
      name: "Maintainer",
      password: "a-password-of-real-length",
    });

    // The claim secret is a credential and travels beside the request rather
    // than inside it, so it is never a field of the thing being made.
    expect(firstRun.headers.get("Vaultaffe-Claim")).toBe(claimSecret);
  });

  // Without it this page would hand the instance to whoever reached it first,
  // which is the window ADR 0007 left open and ADR 0019 closes. The button
  // stays shut rather than sending a request that can only be refused.
  it("will not start the instance until a claim secret is in the form", async () => {
    installInstance({ "GET /api/v1/instance": unstarted });

    renderAt("/start", <Doorway onSignedIn={() => undefined} />);

    await userEvent.type(await screen.findByLabelText("Your name"), "Maintainer");
    await userEvent.type(screen.getByLabelText("Email"), "maintainer@example.test");
    await userEvent.type(screen.getByLabelText("Password"), "a-password-of-real-length");

    expect(screen.getByRole("button", { name: "Start the instance" })).toBeDisabled();

    await userEvent.type(screen.getByLabelText("Claim secret"), claimSecret);

    expect(screen.getByRole("button", { name: "Start the instance" })).toBeEnabled();
  });

  // The window between `docker compose up` and this form is real and belongs to
  // the operator, so the screen names it instead of leaving it to a manual.
  it("says where the claim secret is, for an operator who does not know it exists", async () => {
    installInstance({ "GET /api/v1/instance": unstarted });

    renderAt("/start", <Doorway onSignedIn={() => undefined} />);

    expect(await screen.findByText(/docker compose logs vaultaffe/)).toBeInTheDocument();
    expect(screen.getByText(/a lost one is a restart away/)).toBeInTheDocument();
  });

  it("shows the instance's own sentence when the second one is refused", async () => {
    installInstance({
      "GET /api/v1/instance": unstarted,
      "POST /api/v1/instance": {
        status: 409,
        body: {
          type: "/problems/already-started",
          title: "already-started",
          status: 409,
          code: "already-started",
          detail: "This instance has already been started. Sign in, or ask an administrator to add you.",
        },
      },
    });

    renderAt("/start", <Doorway onSignedIn={() => undefined} />);

    await userEvent.type(await screen.findByLabelText("Your name"), "Somebody Else");
    await userEvent.type(screen.getByLabelText("Email"), "somebody@example.test");
    await userEvent.type(screen.getByLabelText("Password"), "a-password-of-real-length");
    await userEvent.type(screen.getByLabelText("Claim secret"), claimSecret);
    await userEvent.click(screen.getByRole("button", { name: "Start the instance" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("has already been started");
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
