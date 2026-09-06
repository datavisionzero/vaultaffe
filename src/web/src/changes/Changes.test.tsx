import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderUnderShell } from "@/shared/testing";
import { Shell } from "@/shell/Shell";

// Through the frame, because the filter is in the address and the address is
// half of what is being tested.

afterEach(() => {
  vi.unstubAllGlobals();
});

const entries = [
  {
    id: "0199a000-0000-7000-8000-000000000030",
    occurredAt: "2026-09-04T09:00:00+00:00",
    action: "value-set",
    identity: {
      id: "0199a000-0000-7000-8000-000000000031",
      type: "agent-token",
      name: "the deploy agent",
    },
    project: "landing-page",
    environment: "prod",
    secret: "DATABASE_URL",
  },
  {
    id: "0199a000-0000-7000-8000-000000000032",
    occurredAt: "2026-09-03T09:00:00+00:00",
    action: "created",
    identity: {
      id: "0199a000-0000-7000-8000-000000000033",
      type: "human-session",
      name: "maintainer",
    },
    project: "landing-page",
    environment: null,
    secret: null,
  },
  // An administrative entry: no place at all, and what it was about instead
  // (ADR 0020).
  {
    id: "0199a000-0000-7000-8000-000000000034",
    occurredAt: "2026-09-02T09:00:00+00:00",
    action: "email-changed",
    identity: {
      id: "0199a000-0000-7000-8000-000000000033",
      type: "human-session",
      name: "maintainer",
    },
    project: null,
    environment: null,
    secret: null,
    about: "moved@example.test",
  },
];

const changes = "GET /api/v1/changes";

describe("the change log", () => {
  // An entry with no project, environment or secret happened to a person, a
  // token or the organization, and what it happened to is in the same column a
  // place would be — a reader scanning the list is asking what was touched
  // (ADR 0020).
  it("puts what an administrative entry was about where a place would be", async () => {
    installInstance({ [changes]: { entries, total: 3 } });

    renderUnderShell("/changes", <Shell />);

    const changed = (await screen.findByText("email-changed")).closest("li")!;

    expect(within(changed).getByText("moved@example.test")).toBeInTheDocument();
    expect(within(changed).queryByText("the organization")).not.toBeInTheDocument();
  });

  it("says what kind of thing acted, not only who", async () => {
    installInstance({ [changes]: { entries, total: 3 } });

    renderUnderShell("/changes", <Shell />);

    // The type is on the row beside the name, because with writing agents that
    // is the interesting half of "who" (Specification §6.5).
    const written = (await screen.findByText("value-set")).closest("li")!;
    expect(within(written).getByText("the deploy agent")).toBeInTheDocument();
    expect(within(written).getByText("agent-token")).toBeInTheDocument();
    expect(within(written).getByText("landing-page/prod/DATABASE_URL")).toBeInTheDocument();

    const created = screen.getByText("created").closest("li")!;
    expect(within(created).getByText("maintainer")).toBeInTheDocument();
    expect(within(created).getByText("human-session")).toBeInTheDocument();
    expect(within(created).getByText("landing-page")).toBeInTheDocument();
  });

  it("says that reads are not in it, rather than letting a reader assume they are", async () => {
    installInstance({ [changes]: { entries: [], total: 0 } });

    renderUnderShell("/changes", <Shell />);

    expect(await screen.findByText(/Reads are not in it at all/)).toBeInTheDocument();
    expect(screen.getByText(/never what it was changed to/)).toBeInTheDocument();
  });

  it("asks the instance to narrow the log, and puts the filter in the address", async () => {
    const { calls } = installInstance({ [changes]: { entries, total: 3 } });

    renderUnderShell("/changes", <Shell />);

    await userEvent.type(await screen.findByLabelText("Project"), "landing-page");
    await userEvent.type(screen.getByLabelText("Environment"), "prod");
    await userEvent.click(screen.getByRole("button", { name: "Filter" }));

    const asked = await vi.waitFor(() => {
      const last = new URL(calls[calls.length - 1]!.url);
      expect(last.searchParams.get("environment")).toBe("prod");
      return last;
    });

    expect(asked.searchParams.get("project")).toBe("landing-page");
    expect(screen.getByText("filtered")).toBeInTheDocument();
  });

  it("keeps the narrower filter closed until the wider one is named", async () => {
    installInstance({ [changes]: { entries, total: 3 } });

    renderUnderShell("/changes", <Shell />);

    // `environment` without `project` is a search across every project's `prod`,
    // which the instance refuses rather than guesses at. This screen never asks
    // for it (`docs/api.md`).
    expect(await screen.findByLabelText("Environment")).toBeDisabled();
    expect(screen.getByLabelText("Key")).toBeDisabled();
    expect(screen.getByText("Name a project first.")).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText("Project"), "landing-page");

    expect(screen.getByLabelText("Environment")).toBeEnabled();
    expect(screen.getByLabelText("Key")).toBeDisabled();
  });

  it("pages by offset, newest first, and says where the reader is", async () => {
    const { calls } = installInstance({ [changes]: { entries, total: 120 } });

    renderUnderShell("/changes", <Shell />);

    expect(await screen.findByText("1–3 of 120, newest first")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Newer" })).toBeDisabled();

    await userEvent.click(screen.getByRole("button", { name: "Older" }));

    await vi.waitFor(() => {
      expect(new URL(calls[calls.length - 1]!.url).searchParams.get("offset")).toBe("50");
    });
  });

  it("reads the filter out of the address rather than trusting it", async () => {
    const { calls } = installInstance({ [changes]: { entries, total: 3 } });

    renderUnderShell("/changes?project=landing-page&offset=-7", <Shell />);

    const asked = await vi.waitFor(() => {
      const last = new URL(calls[calls.length - 1]!.url);
      expect(last.searchParams.get("project")).toBe("landing-page");
      return last;
    });

    // An address is somebody's to type: an unusable offset is the first page
    // rather than a refusal.
    expect(asked.searchParams.get("offset")).toBe("0");
  });

  it("makes no request that could answer with a value", async () => {
    const { calls } = installInstance({ [changes]: { entries, total: 3 } });

    renderUnderShell("/changes", <Shell />);

    await screen.findByText("landing-page/prod/DATABASE_URL");

    // The log is one endpoint and it carries no value at all. This screen names
    // a key and still never asks for one — as on every other screen, that is a
    // second request, and it is made from the key's own.
    expect(calls).toHaveLength(1);
    expect(new URL(calls[0]!.url).pathname).toBe("/api/v1/changes");
  });
});
