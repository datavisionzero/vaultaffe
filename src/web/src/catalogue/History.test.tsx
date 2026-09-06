import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderUnderShell } from "@/shared/testing";
import { Shell } from "@/shell/Shell";

// The second half of a key's screen — what it used to hold, and what happened
// to it — and the four purges, which are the one act only a person may do.

afterEach(() => {
  vi.unstubAllGlobals();
});

const key = {
  id: "0199a000-0000-7000-8000-000000000020",
  name: "DATABASE_URL",
  status: "set",
  createdAt: "2026-09-01T09:00:00+00:00",
  valueWrittenAt: "2026-09-04T09:00:00+00:00",
  deletedAt: null,
};

const versions = [
  {
    id: "0199a000-0000-7000-8000-000000000040",
    writtenAt: "2026-09-02T09:00:00+00:00",
    replacedAt: "2026-09-04T09:00:00+00:00",
    expiresAt: "2026-09-07T09:00:00+00:00",
  },
];

const entries = [
  {
    id: "0199a000-0000-7000-8000-000000000041",
    occurredAt: "2026-09-04T09:00:00+00:00",
    action: "value-set",
    identity: {
      id: "0199a000-0000-7000-8000-000000000042",
      type: "agent-token",
      name: "the deploy agent",
    },
    project: "landing-page",
    environment: "prod",
    secret: "DATABASE_URL",
  },
];

const reads = [
  {
    identity: {
      id: "0199a000-0000-7000-8000-000000000043",
      type: "service-token",
      name: "the checkout service",
    },
    firstAt: "2026-09-02T09:00:00+00:00",
    lastAt: "2026-09-05T09:00:00+00:00",
  },
];

const secrets = "GET /api/v1/projects/landing-page/environments/prod/secrets";
const theVersions =
  "/api/v1/projects/landing-page/environments/prod/secrets/DATABASE_URL/versions";
const theAccess = "/api/v1/projects/landing-page/environments/prod/secrets/DATABASE_URL/access";

/** A key's screen, with all three halves of its history answered. */
function aKeyWith(routes: Record<string, unknown> = {}) {
  return installInstance({
    [secrets]: [key],
    [`GET ${theVersions}`]: versions,
    [`GET ${theAccess}`]: reads,
    "GET /api/v1/changes": { entries, total: 1 },
    ...routes,
  } as Parameters<typeof installInstance>[0]);
}

describe("what a key used to hold", () => {
  it("says when, and never what", async () => {
    aKeyWith();

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    expect(await screen.findByText(/kept until/)).toBeInTheDocument();
    expect(screen.getByText(/Five versions or 72 hours/)).toBeInTheDocument();

    // The listing is ids and moments. Five old credentials in one answer would
    // be the bulk disclosure the rest of this product avoids (`docs/api.md`).
    expect(screen.getByText(/never what it was/)).toBeInTheDocument();
  });

  it("names the version it rolls back to, and asks before overwriting what is current", async () => {
    const { calls } = aKeyWith({
      "POST /api/v1/projects/landing-page/environments/prod/secrets/DATABASE_URL/rollback": key,
    });

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Roll back to this" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/takes that version's place in the history/)).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole("button", { name: "Roll back" }));

    const rolled = await vi.waitFor(() => {
      const call = calls.find((one) => one.url.endsWith("/rollback"));
      expect(call).toBeDefined();
      return call!;
    });

    expect(await rolled.clone().json()).toEqual({ versionId: versions[0]!.id });
  });

  it("shows this key's own log, narrowed by the instance rather than by the screen", async () => {
    const { calls } = aKeyWith();

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    expect(await screen.findByText("the deploy agent")).toBeInTheDocument();
    expect(screen.getByText("agent-token")).toBeInTheDocument();

    const asked = new URL(calls.find((one) => one.url.includes("/api/v1/changes"))!.url);
    expect(asked.searchParams.get("project")).toBe("landing-page");
    expect(asked.searchParams.get("environment")).toBe("prod");
    expect(asked.searchParams.get("secret")).toBe("DATABASE_URL");
  });
});

describe("a purge", () => {
  it("says both of the two things nobody else says", async () => {
    aKeyWith();

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Purge the history" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/destroys the undo button/)).toBeInTheDocument();
    expect(within(dialog).getByText(/does not reach last night's backup/)).toBeInTheDocument();
  });

  it("removes what a key used to hold and keeps the key", async () => {
    const { calls } = aKeyWith({
      [`DELETE ${theVersions}`]: {
        project: "landing-page",
        environment: "prod",
        secret: "DATABASE_URL",
        versions: 3,
      },
    });

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Purge the history" }));

    const dialog = await screen.findByRole("dialog");
    await userEvent.click(within(dialog).getByRole("button", { name: "Purge" }));

    expect(await screen.findByText(/3 earlier values are gone from this instance/)).toBeInTheDocument();
    expect(
      calls.some((one) => one.method === "DELETE" && one.url.endsWith("/versions")),
    ).toBe(true);
  });

  it("is offered on a deleted project beside the restore that is still possible", async () => {
    const { calls } = installInstance({
      "GET /api/v1/projects": [
        {
          id: "0199a000-0000-7000-8000-000000000010",
          name: "landing-page",
          createdAt: "2026-09-01T09:00:00+00:00",
          deletedAt: "2026-09-04T09:00:00+00:00",
          environments: [],
        },
      ],
      "POST /api/v1/projects/landing-page/purge": { status: 204 },
    });

    renderUnderShell("/projects", <Shell />);

    await userEvent.click(await screen.findByLabelText("Deleted and recoverable"));
    expect(await screen.findByRole("button", { name: "Restore" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Purge" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/frees the name for something else/)).toBeInTheDocument();
    expect(within(dialog).getByText(/destroys the undo button/)).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole("button", { name: "Purge" }));

    await vi.waitFor(() => {
      expect(calls.some((one) => one.url.endsWith("/projects/landing-page/purge"))).toBe(true);
    });
  });
});

describe("who has read a key", () => {
  it("names the identity, its type, and the two moments — and no count", async () => {
    aKeyWith();

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    // Awaited on the row rather than on the heading: the heading is there while
    // the question is still out, and asserting on it would pass before the
    // answer arrived.
    expect(await screen.findByText("the checkout service")).toBeInTheDocument();

    const section = screen.getByText("Who has read it").closest("section")!;

    expect(within(section).getByText("service-token")).toBeInTheDocument();
    expect(within(section).getByText(/^first /)).toBeInTheDocument();
    expect(within(section).getByText(/^last /)).toBeInTheDocument();
    expect(within(section).queryByText(/\d+ (reads|times)/)).not.toBeInTheDocument();
  });

  /**
   * The line that keeps this from reading as an execution history the product
   * does not have.
   */
  it("says on the screen that it is a summary and not a record of every run", async () => {
    aKeyWith();

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    expect(
      await screen.findByText(/does not prove that anything started with it/),
    ).toBeInTheDocument();
  });

  it("says so when nothing has read it", async () => {
    aKeyWith({ [`GET ${theAccess}`]: [] });

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    expect(await screen.findByText("Nothing has read this key yet.")).toBeInTheDocument();
  });
});
