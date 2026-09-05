import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderUnderShell } from "@/shared/testing";
import { shortcuts } from "./shortcuts";
import { Shell } from "./Shell";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("the frame", () => {
  // The frame renders before any of the organization's data arrives, and it
  // asks the instance for none of it.
  it("stands without asking the instance anything", async () => {
    const { calls } = installInstance({});

    renderUnderShell("/projects", <Shell />);

    expect(await screen.findByRole("navigation", { name: "The organization" })).toBeInTheDocument();
    expect(calls).toHaveLength(0);
  });

  it("leads everywhere the screen matrix names", () => {
    installInstance({});

    renderUnderShell("/projects", <Shell />);

    const nav = screen.getByRole("navigation", { name: "The organization" });
    for (const label of ["Projects", "Change log", "Settings"]) {
      expect(within(nav).getByRole("link", { name: label })).toBeInTheDocument();
    }
  });

  it("lands on the projects rather than on nothing", async () => {
    installInstance({});

    renderUnderShell("/", <Shell />);

    expect(await screen.findByRole("heading", { name: "Projects" })).toBeInTheDocument();
  });

  it("says so at an address it has no screen for", async () => {
    installInstance({});

    renderUnderShell("/nowhere-at-all", <Shell />);

    expect(await screen.findByText("There is nothing at this address.")).toBeInTheDocument();
  });

  it("keeps the settings areas beside each other, each with its own address", async () => {
    installInstance({ "GET /api/v1/users": [], "GET /api/v1/invitations": [] });

    renderUnderShell("/settings/users", <Shell />);

    const nav = screen.getByRole("navigation", { name: "Settings" });
    expect(within(nav).getByRole("link", { name: "Tokens" })).toHaveAttribute("href", "/settings/tokens");
    // The area that is open is the screen standing there — the people of the
    // organization, where an unbuilt placeholder used to be.
    expect(await screen.findByRole("heading", { name: "People" })).toBeInTheDocument();
  });
});

describe("the keys", () => {
  it("opens the palette on the key the header advertises", async () => {
    installInstance({});
    const user = userEvent.setup();

    renderUnderShell("/projects", <Shell />);
    await user.keyboard("{Meta>}k{/Meta}");

    expect(await screen.findByRole("combobox")).toHaveAccessibleName("Search the screens, or type a command");
  });

  it("opens the overview on ?, and the overview lists every key that is bound", async () => {
    installInstance({});
    const user = userEvent.setup();

    renderUnderShell("/projects", <Shell />);
    await user.keyboard("?");

    const dialog = await screen.findByRole("dialog");
    for (const shortcut of shortcuts) {
      expect(within(dialog).getByText(shortcut.what)).toBeInTheDocument();
    }
  });

  // A bare key belongs to whatever is being typed into. Here that is the
  // palette's own field, and elsewhere it is a password or a secret's value.
  it("leaves a bare key alone while something is being typed into", async () => {
    installInstance({});
    const user = userEvent.setup();

    renderUnderShell("/projects", <Shell />);
    await user.keyboard("{Meta>}k{/Meta}");

    const field = await screen.findByRole("combobox");
    // Both letters are bound to a place of their own; inside a field they are
    // letters.
    await user.type(field, "help");

    expect(field).toHaveValue("help");

    await user.keyboard("{Escape}");
    expect(await screen.findByRole("heading", { name: "Projects" })).toBeInTheDocument();
  });

  it("goes to the change log on its own key", async () => {
    installInstance({});
    const user = userEvent.setup();

    renderUnderShell("/projects", <Shell />);
    await user.keyboard("h");

    expect(await screen.findByRole("heading", { name: "Change log" })).toBeInTheDocument();
  });
});

describe("the palette", () => {
  it("goes where a row leads", async () => {
    installInstance({});
    const user = userEvent.setup();

    renderUnderShell("/projects", <Shell />);
    await user.click(screen.getByRole("button", { name: /Search or jump/ }));

    const field = await screen.findByRole("combobox");
    await user.type(field, "change");
    await user.keyboard("{Enter}");

    expect(await screen.findByRole("heading", { name: "Change log" })).toBeInTheDocument();
  });

  it("has nothing in it that could reach a value", async () => {
    installInstance({});
    const user = userEvent.setup();

    renderUnderShell("/projects", <Shell />);
    await user.click(screen.getByRole("button", { name: /Search or jump/ }));

    const field = await screen.findByRole("combobox");
    await user.type(field, "reveal");

    expect(await screen.findByText("Nothing matches.")).toBeInTheDocument();
  });
});
