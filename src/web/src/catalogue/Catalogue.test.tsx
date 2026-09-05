import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderUnderShell } from "@/shared/testing";
import { Shell } from "@/shell/Shell";

// Rendered through the frame rather than on their own: these screens are
// addressed by name — a project, an environment, a key — so the route is part
// of what is being tested.

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

const keys = [
  {
    id: "0199a000-0000-7000-8000-000000000020",
    name: "DATABASE_URL",
    status: "set",
    createdAt: "2026-09-01T09:00:00+00:00",
    valueWrittenAt: "2026-09-04T09:00:00+00:00",
    deletedAt: null,
  },
  {
    id: "0199a000-0000-7000-8000-000000000021",
    name: "SMTP_PASSWORD",
    status: "empty",
    createdAt: "2026-09-01T09:00:00+00:00",
    valueWrittenAt: null,
    deletedAt: null,
  },
];

const secrets = "GET /api/v1/projects/landing-page/environments/prod/secrets";
const oneSecret = "GET /api/v1/projects/landing-page/environments/prod/secrets/DATABASE_URL";

describe("the projects", () => {
  it("shows every project with its environments, each addressed by name", async () => {
    installInstance({ "GET /api/v1/projects": [landingPage] });

    renderUnderShell("/projects", <Shell />);

    expect(await screen.findByRole("link", { name: "landing-page" })).toHaveAttribute(
      "href",
      "/projects/landing-page",
    );
    expect(screen.getByRole("link", { name: "prod" })).toHaveAttribute(
      "href",
      "/projects/landing-page/prod",
    );
  });

  it("asks the instance for what is deleted rather than filtering a list it has", async () => {
    const { calls } = installInstance({ "GET /api/v1/projects": [] });

    renderUnderShell("/projects", <Shell />);

    await userEvent.click(await screen.findByLabelText("Deleted and recoverable"));

    expect(
      await screen.findByText(/Nothing here has been deleted in the last 72 hours/),
    ).toBeInTheDocument();
    expect(calls.some((call) => new URL(call.url).searchParams.get("deleted") === "true")).toBe(true);
  });

  it("says what is kept and for how long before deleting one", async () => {
    installInstance({ "GET /api/v1/projects": [landingPage] });

    renderUnderShell("/projects", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Delete" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/restored for 72 hours/)).toBeInTheDocument();
  });
});

describe("a project", () => {
  it("counts the keys of an environment without ever asking for a value", async () => {
    const { calls } = installInstance({
      "GET /api/v1/projects/landing-page": landingPage,
      [secrets]: keys,
    });

    renderUnderShell("/projects/landing-page", <Shell />);

    expect(await screen.findByText(/2 keys/)).toBeInTheDocument();
    expect(screen.getByText(/1 waiting for a value/)).toBeInTheDocument();

    // The listing endpoint, and nothing under it: names and status are one
    // request, a value is another and is never made from here.
    expect(calls.every((call) => !call.url.includes("/secrets/"))).toBe(true);
  });
});

describe("the environment screen", () => {
  it("shows names and status, and has no value in it at all", async () => {
    const { calls } = installInstance({ [secrets]: keys });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    expect(await screen.findByRole("link", { name: "DATABASE_URL" })).toHaveAttribute(
      "href",
      "/projects/landing-page/prod/DATABASE_URL",
    );

    // The status is a word, and the empty one says what it means rather than
    // reading as an error.
    expect(screen.getByText("set")).toBeInTheDocument();
    expect(screen.getByText("empty")).toBeInTheDocument();
    expect(screen.getByText("waiting for a person to fill it")).toBeInTheDocument();

    expect(calls).toHaveLength(1);
  });

  it("offers import beside the first key, because that is where teams arrive from", async () => {
    installInstance({ [secrets]: [] });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    expect(await screen.findByText("No keys in this environment yet.")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Import" })).not.toHaveLength(0);
  });

  it("turns the instance's replace-required into the question it is", async () => {
    installInstance({
      [secrets]: keys,
      "PUT /api/v1/projects/landing-page/environments/prod/secrets/DATABASE_URL": {
        status: 409,
        body: {
          type: "/problems/replace-required",
          title: "replace-required",
          status: 409,
          code: "replace-required",
          detail: "'DATABASE_URL' already holds a value.",
          secretName: "DATABASE_URL",
        },
      },
    });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "New key" }));

    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByLabelText("Key"), "DATABASE_URL");
    await userEvent.type(within(dialog).getByLabelText("Value"), "postgres://somewhere");
    await userEvent.click(within(dialog).getByRole("button", { name: "Save the value" }));

    expect(await screen.findByText("Overwrite DATABASE_URL?")).toBeInTheDocument();
    expect(screen.getByText(/kept as a version for 72 hours/)).toBeInTheDocument();
  });

  it("asks before an export, and says what it is about to write", async () => {
    installInstance({ [secrets]: keys });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Export" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/every value of this environment in plaintext/)).toBeInTheDocument();
    expect(within(dialog).queryByText(/don't ask again/i)).not.toBeInTheDocument();
  });

  it("answers an import with names and never with values", async () => {
    installInstance({
      [secrets]: [],
      "POST /api/v1/projects/landing-page/environments/prod/import": {
        created: ["DATABASE_URL"],
        filled: [],
        replaced: [],
        unchanged: [],
        skipped: [{ name: "API_KEY", reason: "it already holds a value" }],
        unreadable: [{ line: 4, reason: "no = on this line" }],
      },
    });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    await userEvent.click((await screen.findAllByRole("button", { name: "Import" }))[0]!);

    const dialog = await screen.findByRole("dialog");
    await userEvent.type(
      within(dialog).getByLabelText("The contents of a .env file"),
      "DATABASE_URL=postgres://somewhere",
    );
    await userEvent.click(within(dialog).getByRole("button", { name: "Import" }));

    expect(await screen.findByText("What the file did")).toBeInTheDocument();
    expect(screen.getByText("DATABASE_URL")).toBeInTheDocument();
    expect(screen.getByText(/line 4 — no = on this line/)).toBeInTheDocument();
    expect(screen.queryByText(/postgres:\/\/somewhere/)).not.toBeInTheDocument();
  });
});

describe("one key", () => {
  it("holds no value until a person asks for one", async () => {
    const { calls } = installInstance({ [secrets]: keys });

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    expect(await screen.findByText("Value, hidden.")).toBeInTheDocument();
    expect(calls.every((call) => !call.url.endsWith("/secrets/DATABASE_URL"))).toBe(true);
  });

  it("reveals one key with one request, and takes it back", async () => {
    installInstance({
      [secrets]: keys,
      [oneSecret]: { secret: keys[0], value: "postgres://the-real-thing" },
    });

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Reveal it" }));

    expect(await screen.findByText("postgres://the-real-thing")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Hide it again" }));

    expect(screen.queryByText("postgres://the-real-thing")).not.toBeInTheDocument();
    expect(screen.getByText("Value, hidden.")).toBeInTheDocument();
  });

  // `docs/human-interface.md`: copying is revealing, and the control says so.
  it("says that copying is reading", async () => {
    installInstance({ [secrets]: keys });

    renderUnderShell("/projects/landing-page/prod/DATABASE_URL", <Shell />);

    // The visible words are the accessible name: a control that says one thing
    // and announces another is two labels for one act.
    expect(await screen.findByRole("button", { name: "Read it and copy" })).toBeInTheDocument();
  });

  it("says what an empty key means rather than showing an error", async () => {
    installInstance({ [secrets]: keys });

    renderUnderShell("/projects/landing-page/prod/SMTP_PASSWORD", <Shell />);

    expect(await screen.findByText("This key is empty")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Fill it" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reveal it" })).not.toBeInTheDocument();
  });
});
