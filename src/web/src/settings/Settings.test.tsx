import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "@/api/client";
import { aPerson, installInstance, renderUnderShell } from "@/shared/testing";
import { Shell } from "@/shell/Shell";

afterEach(() => {
  vi.unstubAllGlobals();
});

const organization = {
  id: aPerson.organizationId,
  name: "Default",
  createdAt: "2026-09-01T09:00:00+00:00",
};

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
    {
      id: "0199a000-0000-7000-8000-000000000012",
      projectId: "0199a000-0000-7000-8000-000000000010",
      name: "dev",
      createdAt: "2026-09-01T09:00:00+00:00",
      deletedAt: null,
    },
  ],
};

const theAgent = {
  id: "0199a000-0000-7000-8000-000000000030",
  kind: "agent",
  name: "the agent in my terminal",
  scopes: ["names", "read", "write", "delete"],
  bindings: [],
  reachesTheWholeOrganization: true,
  createdAt: "2026-09-04T09:00:00+00:00",
  expiresAt: null,
  revokedAt: null,
};

const aMember: Me = { ...aPerson, isAdministrator: false };

describe("the tokens", () => {
  it("says what a token is, what it reaches, and where it stands", async () => {
    installInstance({
      "GET /api/v1/tokens": [
        theAgent,
        { ...theAgent, id: "0199a000-0000-7000-8000-000000000031", name: "an old one", revokedAt: "2026-09-04T10:00:00+00:00" },
      ],
      "GET /api/v1/projects": [landingPage],
    });

    renderUnderShell("/settings/tokens", <Shell />);

    expect(await screen.findByText("the agent in my terminal")).toBeInTheDocument();
    expect(screen.getAllByText("names read write delete")).toHaveLength(2);
    expect(screen.getAllByText(/the whole organization/)).not.toHaveLength(0);

    // Standing is a word, not a colour: a revoked token says revoked.
    expect(screen.getByText("revoked")).toBeInTheDocument();
  });

  it("hands the value over once and says that this is the once", async () => {
    installInstance({
      "GET /api/v1/tokens": [],
      "GET /api/v1/projects": [landingPage],
      "POST /api/v1/tokens": { token: theAgent, value: "vaultaffe_agent_thevalueitself" },
    });

    renderUnderShell("/settings/tokens", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "New token" }));

    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText("Name"), "the agent in my terminal");
    await userEvent.click(within(dialog).getByRole("button", { name: "Create the token" }));

    expect(await screen.findByText(/This is the once/)).toBeInTheDocument();
    expect(screen.getByText("vaultaffe_agent_thevalueitself")).toBeInTheDocument();
  });

  // §6.4: the default is the whole organization with every scope, and excluding
  // production is the first narrowing offered.
  it("offers keeping an agent out of production, and says what that binds to", async () => {
    installInstance({ "GET /api/v1/tokens": [], "GET /api/v1/projects": [landingPage] });

    renderUnderShell("/settings/tokens", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "New token" }));

    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByLabelText(/Agent/)).toBeChecked();
    expect(within(dialog).getByLabelText(/The whole organization/)).toBeChecked();
    expect(within(dialog).getByText(/every one not called prod or production/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Binds it to 1 environments/)).toBeInTheDocument();
  });

  it("sends the environments an exclusion actually means", async () => {
    const { calls } = installInstance({
      "GET /api/v1/tokens": [],
      "GET /api/v1/projects": [landingPage],
      "POST /api/v1/tokens": { token: theAgent, value: "vaultaffe_agent_thevalueitself" },
    });

    renderUnderShell("/settings/tokens", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "New token" }));

    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText("Name"), "kept out of production");
    await userEvent.click(within(dialog).getByLabelText(/Everything except production/));
    await userEvent.click(within(dialog).getByRole("button", { name: "Create the token" }));

    const created = calls.find(
      (call) => call.method === "POST" && call.url.endsWith("/api/v1/tokens"),
    )!;

    expect(await created.clone().json()).toMatchObject({
      kind: "agent",
      bindings: [
        {
          projectId: landingPage.id,
          environmentId: landingPage.environments[1]!.id,
        },
      ],
    });
  });

  // Two lists and not one: a session is what every sign-in leaves behind, and in
  // one list with the few named credentials it would bury them by sheer number.
  it("keeps the sessions apart from the tokens an agent or a service acts under", async () => {
    installInstance({
      "GET /api/v1/tokens": [
        { ...theAgent, id: aPerson.tokenId, kind: "session", name: null },
        {
          ...theAgent,
          id: "0199a000-0000-7000-8000-000000000032",
          kind: "session",
          name: null,
          createdAt: "2026-09-02T09:00:00+00:00",
        },
        theAgent,
        {
          ...theAgent,
          id: "0199a000-0000-7000-8000-000000000033",
          kind: "service",
          name: "the deployment",
        },
      ],
      "GET /api/v1/projects": [landingPage],
    });

    renderUnderShell("/settings/tokens", <Shell />);

    const tokens = await screen.findByRole("region", { name: "Tokens" });
    const sessions = screen.getByRole("region", { name: "Sessions" });

    // What is named sits in the first list, and the kind still tells the two of
    // them apart.
    expect(within(tokens).getByText("the agent in my terminal")).toBeInTheDocument();
    expect(within(tokens).getByText("the deployment")).toBeInTheDocument();
    expect(within(tokens).getByText("agent")).toBeInTheDocument();
    expect(within(tokens).getByText("service")).toBeInTheDocument();
    expect(within(tokens).queryByText("A signed-in session")).not.toBeInTheDocument();

    // The sign-ins sit in the second, this browser's marked, and no chip repeats
    // the heading on every row of them.
    expect(within(sessions).getAllByText("A signed-in session")).toHaveLength(2);
    expect(within(sessions).getByText("\u00b7 this browser")).toBeInTheDocument();
    expect(within(sessions).queryByText("session")).not.toBeInTheDocument();

    // Creating belongs to the first list: this screen issues an agent or a
    // service token and never a session.
    expect(within(tokens).getByRole("button", { name: "New token" })).toBeInTheDocument();
  });

  it("says so where a list has nothing in it, and which kind of nothing it is", async () => {
    installInstance({
      "GET /api/v1/tokens": [{ ...theAgent, id: aPerson.tokenId, kind: "session", name: null }],
      "GET /api/v1/projects": [],
    });

    renderUnderShell("/settings/tokens", <Shell />);

    // Not "there is nothing": what is hidden is said, because somebody whose
    // tokens are all revoked has no way to see through the shorter sentence.
    expect(
      await screen.findByText("No agent or service token that works. Revoked ones are hidden."),
    ).toBeInTheDocument();
  });

  // A revoked token keeps its row for good — everything it signed in the change
  // log keeps an author that way — and that is a reason to keep it, not a reason
  // to keep it in front of the credentials somebody came here to read.
  it("leaves the revoked out until they are asked for", async () => {
    const retired = {
      ...theAgent,
      id: "0199a000-0000-7000-8000-000000000034",
      name: "the agent that was",
      revokedAt: "2026-09-01T09:00:00+00:00",
    };

    const { calls } = installInstance({
      "GET /api/v1/tokens": (request: Request) =>
        new URL(request.url).searchParams.get("revoked") === "true"
          ? [theAgent, retired]
          : [theAgent],
      "GET /api/v1/projects": [],
    });

    renderUnderShell("/settings/tokens", <Shell />);

    expect(await screen.findByText("the agent in my terminal")).toBeInTheDocument();
    expect(screen.queryByText("the agent that was")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("checkbox", { name: "Show revoked" }));

    // The screen asks the instance again rather than filtering what it already
    // holds: what the instance holds is what the screen shows.
    expect(await screen.findByText("the agent that was")).toBeInTheDocument();
    expect(
      calls.some((call) => new URL(call.url).searchParams.get("revoked") === "true"),
    ).toBe(true);
  });

  // The one thing changing cannot touch. What survives is the credential: the
  // name, the scopes and the reach come across, and what goes is the value that
  // somebody had reason to worry about.
  it("rotates a token and shows the new value once", async () => {
    const { calls } = installInstance({
      "GET /api/v1/tokens": [theAgent],
      "GET /api/v1/projects": [],
      [`POST /api/v1/tokens/${theAgent.id}/rotate`]: {
        token: { ...theAgent, id: "0199a000-0000-7000-8000-000000000035" },
        value: "vaultaffe_agent_theonethatreplacesit",
      },
    });

    renderUnderShell("/settings/tokens", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Rotate" }));

    // It asks first, and the question says what it costs rather than asserting
    // that it is safe.
    expect(
      await screen.findByText(/The value it has now stops working immediately/),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Rotate it" }));

    expect(await screen.findByText("vaultaffe_agent_theonethatreplacesit")).toBeInTheDocument();
    expect(
      calls.some(
        (call) =>
          call.method === "POST" && call.url.endsWith(`/api/v1/tokens/${theAgent.id}/rotate`),
      ),
    ).toBe(true);

    // And the way out of that screen is a button, not a scrollbar.
    expect(screen.getByRole("button", { name: "Done" })).toBeInTheDocument();
  });

  // The act that exists because the value cannot change: a token that has to
  // reach one more project is amended, not reissued and copied round again.
  it("changes what a token reaches without touching its value", async () => {
    const { calls } = installInstance({
      "GET /api/v1/tokens": [theAgent],
      "GET /api/v1/projects": [landingPage],
      [`PATCH /api/v1/tokens/${theAgent.id}`]: {
        ...theAgent,
        reachesTheWholeOrganization: false,
        bindings: [{ projectId: landingPage.id, environmentId: null }],
      },
    });

    renderUnderShell("/settings/tokens", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Change" }));

    const dialog = await screen.findByRole("dialog");

    // It opens on what is true of the token rather than on a blank form.
    expect(within(dialog).getByLabelText("Name")).toHaveValue("the agent in my terminal");
    expect(within(dialog).getByLabelText(/The whole organization/)).toBeChecked();
    expect(within(dialog).getByText(/value does not change/)).toBeInTheDocument();

    await userEvent.click(within(dialog).getByLabelText(/Only what I pick/));
    await userEvent.click(within(dialog).getByLabelText(/landing-page/));
    await userEvent.click(within(dialog).getByRole("button", { name: "Save the change" }));

    const changed = calls.find((call) => call.method === "PATCH")!;

    // The whole project, and not a list of the environments it happens to have
    // today: one added tomorrow is covered, which is what picking a project
    // means.
    expect(await changed.clone().json()).toMatchObject({
      name: "the agent in my terminal",
      scopes: ["names", "read", "write", "delete"],
      bindings: [{ projectId: landingPage.id, environmentId: null }],
    });
  });

  // A revocation list that let something vanish while it still worked would be
  // the one list nobody could trust, so the order is fixed: revoke, then delete.
  it("offers deleting the row only once a token is revoked", async () => {
    const { calls } = installInstance({
      "GET /api/v1/tokens": [
        theAgent,
        {
          ...theAgent,
          id: "0199a000-0000-7000-8000-000000000031",
          name: "an old one",
          revokedAt: "2026-09-04T10:00:00+00:00",
        },
      ],
      "GET /api/v1/projects": [landingPage],
      "POST /api/v1/tokens/0199a000-0000-7000-8000-000000000031/purge": theAgent,
    });

    renderUnderShell("/settings/tokens", <Shell />);

    const tokens = await screen.findByRole("region", { name: "Tokens" });
    await within(tokens).findByText("an old one");

    const rows = within(tokens).getAllByRole("listitem");
    const inUse = rows.find((row) => within(row).queryByText("the agent in my terminal"))!;
    const revoked = rows.find((row) => within(row).queryByText("an old one"))!;

    // What still works can be changed and revoked, and not deleted.
    expect(within(inUse).getByRole("button", { name: "Change" })).toBeInTheDocument();
    expect(within(inUse).getByRole("button", { name: "Revoke" })).toBeInTheDocument();
    expect(within(inUse).queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();

    // What is revoked can only go. There is nothing left to arrange about a
    // credential that authenticates nothing.
    expect(within(revoked).queryByRole("button", { name: "Change" })).not.toBeInTheDocument();
    expect(within(revoked).queryByRole("button", { name: "Revoke" })).not.toBeInTheDocument();

    await userEvent.click(within(revoked).getByRole("button", { name: "Delete" }));

    expect(await screen.findByText(/change log keeps everything it did/i)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Delete for good" }));

    expect(
      calls.some((call) => call.method === "POST" && call.url.endsWith("/purge")),
    ).toBe(true);
  });

  it("says what revoking the session of this browser would do", async () => {
    installInstance({
      "GET /api/v1/tokens": [{ ...theAgent, id: aPerson.tokenId, kind: "session", name: null }],
      "GET /api/v1/projects": [],
    });

    renderUnderShell("/settings/tokens", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Revoke" }));

    expect(await screen.findByText(/signs you out here, immediately/)).toBeInTheDocument();
  });
});

describe("the organization", () => {
  it("renames it, and tells a member who can", async () => {
    installInstance({ "GET /api/v1/organization": organization });

    renderUnderShell("/settings/organization", <Shell />, aMember);

    expect(await screen.findByLabelText("Name")).toBeDisabled();
    expect(screen.getByText(/Renaming the organization is an administrator's/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Rename" })).not.toBeInTheDocument();
  });

  it("lets an administrator rename it", async () => {
    const { calls } = installInstance({
      "GET /api/v1/organization": organization,
      "PATCH /api/v1/organization": { ...organization, name: "The Small Team" },
    });

    renderUnderShell("/settings/organization", <Shell />);

    const field = await screen.findByLabelText("Name");
    await userEvent.clear(field);
    await userEvent.type(field, "The Small Team");
    await userEvent.click(screen.getByRole("button", { name: "Rename" }));

    expect(await screen.findByText("Renamed.")).toBeInTheDocument();
    expect(
      calls.some((call) => call.method === "PATCH" && call.url.endsWith("/api/v1/organization")),
    ).toBe(true);
  });
});

describe("the profile", () => {
  it("says what changing a password costs before it is changed", async () => {
    installInstance({});

    renderUnderShell("/settings/profile", <Shell />);

    expect(
      await screen.findByText(/ends every other session you are signed in with/),
    ).toBeInTheDocument();
  });

  it("asks for the current password, and forgets both afterwards", async () => {
    installInstance({ "POST /api/v1/me/password": { status: 204 } });

    renderUnderShell("/settings/profile", <Shell />);

    // Two forms on this screen ask for the password somebody already has, so
    // each act is reached through the block it belongs to.
    const block = await screen.findByRole("region", { name: "Password" });

    const current = within(block).getByLabelText("Your current password");
    await userEvent.type(current, "a-password-of-real-length");
    await userEvent.type(within(block).getByLabelText("Your new password"), "a-longer-new-password");
    await userEvent.click(within(block).getByRole("button", { name: "Change it" }));

    expect(await screen.findByText(/Your other sessions have ended/)).toBeInTheDocument();
    expect(current).toHaveValue("");
    expect(within(block).getByLabelText("Your new password")).toHaveValue("");
  });

  // The second way to the address: the one that needs nobody else.
  it("changes the address you sign in with, and keeps the sessions", async () => {
    const { calls } = installInstance({
      "POST /api/v1/me/email": {
        id: aPerson.userId,
        email: "maintainer@elsewhere.test",
        name: aPerson.name,
        isAdministrator: true,
        createdAt: "2026-09-01T09:00:00+00:00",
        deactivatedAt: null,
      },
    });

    renderUnderShell("/settings/profile", <Shell />);

    const block = await screen.findByRole("region", { name: "The address you sign in with" });

    // It starts at the address they have, and changing nothing is not an act.
    const field = within(block).getByLabelText("The address you will sign in with");
    expect(field).toHaveValue("maintainer@example.test");
    expect(within(block).getByRole("button", { name: "Change it" })).toBeDisabled();

    await userEvent.clear(field);
    await userEvent.type(field, "maintainer@elsewhere.test");
    await userEvent.type(
      within(block).getByLabelText("Your current password"),
      "a-password-of-real-length",
    );
    await userEvent.click(within(block).getByRole("button", { name: "Change it" }));

    expect(await screen.findByText(/your sessions all stay/)).toBeInTheDocument();

    const sent = calls.find((call) => call.url.endsWith("/api/v1/me/email"));
    expect(sent).toBeDefined();

    // The password is not left in the field it was typed into.
    expect(within(block).getByLabelText("Your current password")).toHaveValue("");
  });

  it("tells the frame the new name rather than leaving the old one in it", async () => {
    installInstance({ "PATCH /api/v1/me": { ...aPerson, id: aPerson.userId, name: "The Maintainer", email: "maintainer@example.test", createdAt: "2026-09-01T09:00:00+00:00", deletedAt: null } });

    renderUnderShell("/settings/profile", <Shell />);

    const field = await screen.findByLabelText("Your name");
    await userEvent.clear(field);
    await userEvent.type(field, "The Maintainer");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Saved.")).toBeInTheDocument();
  });
});
