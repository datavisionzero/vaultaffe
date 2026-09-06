import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "@/api/client";
import { aPerson, installInstance, renderUnderShell } from "@/shared/testing";
import { Users } from "./Users";

afterEach(() => {
  vi.unstubAllGlobals();
});

const maintainer = {
  id: aPerson.userId,
  email: "maintainer@example.test",
  name: "Maintainer",
  isAdministrator: true,
  createdAt: "2026-09-01T09:00:00+00:00",
  deactivatedAt: null,
};

const newcomer = {
  id: "0199a000-0000-7000-8000-00000000000a",
  email: "newcomer@example.test",
  name: "Newcomer",
  isAdministrator: false,
  createdAt: "2026-09-03T09:00:00+00:00",
  deactivatedAt: null,
};

/** Somebody who is in the organization and does not administer it. */
const aMember: Me = { ...aPerson, isAdministrator: false, name: "Newcomer", userId: newcomer.id };

describe("the people of the organization", () => {
  it("says who is here and what the one line between them is", async () => {
    installInstance({
      "GET /api/v1/users": [maintainer, newcomer],
      "GET /api/v1/invitations": [],
    });

    renderUnderShell("/settings/users", <Users />);

    expect(await screen.findByText("Maintainer")).toBeInTheDocument();
    expect(screen.getByText("Administrator")).toBeInTheDocument();
    expect(screen.getByText("Member of the organization")).toBeInTheDocument();
  });

  // `docs/human-interface.md`: status is carried by a word, never by a colour
  // alone, and a deleted or deactivated thing says so in text.
  it("says in words that somebody is deactivated", async () => {
    installInstance({
      "GET /api/v1/users": [
        maintainer,
        { ...newcomer, deactivatedAt: "2026-09-04T09:00:00+00:00" },
      ],
      "GET /api/v1/invitations": [],
    });

    renderUnderShell("/settings/users", <Users />);

    expect(await screen.findByText(/deactivated/)).toBeInTheDocument();
  });

  it("hands over the invitation link once, and says that this is the once", async () => {
    installInstance({
      "GET /api/v1/users": [maintainer],
      "GET /api/v1/invitations": [],
      "POST /api/v1/invitations": {
        invitation: {
          id: "0199a000-0000-7000-8000-00000000000b",
          email: "newcomer@example.test",
          name: "Newcomer",
          isAdministrator: false,
          state: "open",
          invitedByUserId: maintainer.id,
          createdAt: "2026-09-05T09:00:00+00:00",
          expiresAt: "2026-09-08T09:00:00+00:00",
        },
        link: "/invite#the-code-itself",
      },
    });

    renderUnderShell("/settings/users", <Users />);

    await userEvent.click(await screen.findByRole("button", { name: "Invite somebody" }));

    const dialog = screen.getByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText("Email"), "newcomer@example.test");
    await userEvent.type(within(dialog).getByLabelText("Name"), "Newcomer");
    await userEvent.click(within(dialog).getByRole("button", { name: "Write the invitation" }));

    expect(await screen.findByText(/This is the once/)).toBeInTheDocument();

    // Absolute, because the instance answers a relative link — it does not know
    // what address a browser reached it at, and this application does.
    expect(screen.getByText(/\/invite#the-code-itself$/)).toBeInTheDocument();
  });

  // The control is disabled with the reason beside it rather than hidden: a
  // control that vanishes is a question nobody can ask.
  it("tells a member why an act is not theirs instead of hiding it", async () => {
    installInstance({ "GET /api/v1/users": [maintainer, newcomer] });

    renderUnderShell("/settings/users", <Users />, aMember);

    await userEvent.click(await screen.findByRole("button", { name: "Actions for Maintainer" }));

    // Every act of the row is there, and each says the same reason: it is the
    // instance's rule, said before it has to say it.
    expect(
      await screen.findAllByText("Administering the organization and its people is an administrator's."),
    ).toHaveLength(3);
    expect(screen.getByText("Reset their password…")).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "Reset their password…" })).not.toBeInTheDocument();
  });

  // The address is the login name, and the dialog has to say that before it is
  // changed: what moves is what they sign in with, and nothing else does.
  it("changes an address, and says that the password and the sessions stay", async () => {
    const { calls } = installInstance({
      "GET /api/v1/users": [maintainer, newcomer],
      "GET /api/v1/invitations": [],
      [`POST /api/v1/users/${newcomer.id}/email`]: {
        ...newcomer,
        email: "newcomer@elsewhere.test",
      },
    });

    renderUnderShell("/settings/users", <Users />);

    await userEvent.click(await screen.findByRole("button", { name: "Actions for Newcomer" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Change their address…" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/not one this instance sends anything to/)).toBeInTheDocument();
    expect(within(dialog).getByText(/password and every session and token/)).toBeInTheDocument();

    // It opens on the address they have, and changing nothing is not an act.
    const field = within(dialog).getByLabelText("The address they will sign in with");
    expect(field).toHaveValue("newcomer@example.test");
    expect(within(dialog).getByRole("button", { name: "Change it" })).toBeDisabled();

    await userEvent.clear(field);
    await userEvent.type(field, "newcomer@elsewhere.test");
    await userEvent.click(within(dialog).getByRole("button", { name: "Change it" }));

    expect(calls.some((call) => call.url.endsWith(`/users/${newcomer.id}/email`))).toBe(true);
  });

  it("says why nobody deactivates themselves", async () => {
    installInstance({
      "GET /api/v1/users": [maintainer, newcomer],
      "GET /api/v1/invitations": [],
    });

    renderUnderShell("/settings/users", <Users />);

    await userEvent.click(await screen.findByRole("button", { name: "Actions for Maintainer" }));

    expect(await screen.findByText(/Nobody deactivates themselves/)).toBeInTheDocument();
  });

  it("asks before deactivating, and says what it takes with it", async () => {
    const { calls } = installInstance({
      "GET /api/v1/users": [maintainer, newcomer],
      "GET /api/v1/invitations": [],
      [`POST /api/v1/users/${newcomer.id}/deactivate`]: {
        ...newcomer,
        deactivatedAt: "2026-09-05T10:00:00+00:00",
      },
    });

    renderUnderShell("/settings/users", <Users />);

    await userEvent.click(await screen.findByRole("button", { name: "Actions for Newcomer" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Deactivate…" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/every token of theirs stops working/)).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole("button", { name: "Deactivate" }));

    expect(
      calls.some((call) => call.url.endsWith(`/users/${newcomer.id}/deactivate`)),
    ).toBe(true);
  });
});
