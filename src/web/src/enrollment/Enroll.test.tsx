import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { aPerson, installInstance, renderUnderShell } from "@/shared/testing";
import { Shell } from "@/shell/Shell";

afterEach(() => {
  vi.unstubAllGlobals();
});

const landingPage = {
  id: "0199a000-0000-7000-8000-000000000010",
  name: "landing-page",
  createdAt: "2026-09-01T09:00:00+00:00",
  deletedAt: null,
  environments: [
    {
      id: "0199a000-0000-7000-8000-000000000011",
      projectId: "0199a000-0000-7000-8000-000000000010",
      name: "prod",
      createdAt: "2026-09-01T09:00:00+00:00",
      deletedAt: null,
    },
  ],
};

const waiting = {
  requestedName: "claude in ~/webshop",
  askedAt: "2026-09-09T09:00:00+00:00",
  expiresAt: "2026-09-09T09:10:00+00:00",
  state: "pending",
};

describe("deciding an enrollment", () => {
  it("says what asked, that the instance cannot check it, and that nothing has to be copied", async () => {
    installInstance({
      "GET /api/v1/enrollments/BCDF-GHJK": waiting,
      "GET /api/v1/projects": [landingPage],
    });

    renderUnderShell("/enroll?code=BCDF-GHJK", <Shell />, aPerson);

    expect(await screen.findByText("claude in ~/webshop")).toBeInTheDocument();

    // The name is a claim, and the screen refuses to dress it up as a fact.
    expect(screen.getByText(/no way to check it/)).toBeInTheDocument();

    // And the sentence the whole flow exists for.
    expect(screen.getByText(/never shown here/)).toBeInTheDocument();
  });

  it("agrees with the scopes and the reach a person chose", async () => {
    const { calls } = installInstance({
      "GET /api/v1/enrollments/BCDF-GHJK": waiting,
      "GET /api/v1/projects": [landingPage],
      "POST /api/v1/enrollments/BCDF-GHJK/approval": { ...waiting, state: "approved" },
    });

    renderUnderShell("/enroll?code=BCDF-GHJK", <Shell />, aPerson);

    const name = await screen.findByLabelText("Name");

    // It opens on what the machine asked to be called, and a person settles it.
    expect(name).toHaveValue("claude in ~/webshop");

    await userEvent.clear(name);
    await userEvent.type(name, "the agent on the webshop");
    await userEvent.click(screen.getByLabelText(/^delete/));
    await userEvent.click(screen.getByRole("button", { name: /Agree/ }));

    const agreed = calls.find((call) => call.url.endsWith("/approval"))!;

    expect(await agreed.clone().json()).toMatchObject({
      name: "the agent on the webshop",
      scopes: ["names", "read", "write"],
      bindings: [],
    });
  });

  it("says no on a person's behalf, in the words they would use", async () => {
    const { calls } = installInstance({
      "GET /api/v1/enrollments/BCDF-GHJK": waiting,
      "GET /api/v1/projects": [landingPage],
      "POST /api/v1/enrollments/BCDF-GHJK/refusal": { ...waiting, state: "denied" },
    });

    renderUnderShell("/enroll?code=BCDF-GHJK", <Shell />, aPerson);

    await userEvent.click(await screen.findByRole("button", { name: /did not start this/ }));

    expect(calls.some((call) => call.url.endsWith("/refusal"))).toBe(true);
  });

  it("has nothing left to decide about one somebody already decided", async () => {
    installInstance({
      "GET /api/v1/enrollments/BCDF-GHJK": { ...waiting, state: "redeemed" },
      "GET /api/v1/projects": [landingPage],
    });

    renderUnderShell("/enroll?code=BCDF-GHJK", <Shell />, aPerson);

    expect(await screen.findByText(/has collected its token/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Agree/ })).not.toBeInTheDocument();
  });

  it("asks for the code when nobody arrived with one", async () => {
    installInstance({ "GET /api/v1/projects": [landingPage] });

    renderUnderShell("/enroll", <Shell />, aPerson);

    const field = await screen.findByLabelText("The code");

    expect(field).toBeInTheDocument();
    expect(within(document.body).getByRole("button", { name: "Look it up" })).toBeDisabled();
  });
});
