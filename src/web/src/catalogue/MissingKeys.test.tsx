import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { installInstance, renderUnderShell } from "@/shared/testing";
import { Shell } from "@/shell/Shell";

afterEach(() => {
  vi.unstubAllGlobals();
});

const secrets = "GET /api/v1/projects/landing-page/environments/prod/secrets";
const missing = "GET /api/v1/projects/landing-page/environments/prod/missing";

const key = {
  id: "0199a000-0000-7000-8000-000000000020",
  name: "DATABASE_URL",
  status: "set",
  createdAt: "2026-09-01T09:00:00+00:00",
  valueWrittenAt: "2026-09-04T09:00:00+00:00",
  deletedAt: null,
};

describe("the missing-key notice", () => {
  it("says which key is missing and where it is, and nothing about a value", async () => {
    installInstance({
      [secrets]: [key],
      [missing]: [{ name: "STRIPE_KEY", presentIn: ["dev", "staging"], dismissedAt: null }],
    });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    const notice = await screen.findByRole("region", {
      name: "Keys missing in this environment",
    });

    expect(within(notice).getByText("STRIPE_KEY")).toBeInTheDocument();
    expect(within(notice).getByText("in dev, staging")).toBeInTheDocument();
  });

  /**
   * The whole point of §6.1: it displays. There is no button here that writes a
   * key by itself, and adding one is the same form as any other write.
   */
  it("offers to add the key and to say not here, and nothing that writes by itself", async () => {
    installInstance({
      [secrets]: [key],
      [missing]: [{ name: "STRIPE_KEY", presentIn: ["dev", "staging"], dismissedAt: null }],
    });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    const notice = await screen.findByRole("region", {
      name: "Keys missing in this environment",
    });

    expect(within(notice).getByRole("button", { name: "Add it here" })).toBeInTheDocument();
    expect(within(notice).getByRole("button", { name: "Not here" })).toBeInTheDocument();
    expect(within(notice).queryByRole("button", { name: /add all/i })).not.toBeInTheDocument();
  });

  it("dismisses one key, and offers to mention it again", async () => {
    const { calls } = installInstance({
      [secrets]: [key],
      [missing]: () =>
        calls.some((call) => call.method === "POST" && call.url.includes("/dismissal"))
          ? [
              {
                name: "STRIPE_KEY",
                presentIn: ["dev", "staging"],
                dismissedAt: "2026-09-06T09:00:00+00:00",
              },
            ]
          : [{ name: "STRIPE_KEY", presentIn: ["dev", "staging"], dismissedAt: null }],
      "POST /api/v1/projects/landing-page/environments/prod/missing/STRIPE_KEY/dismissal": {
        name: "STRIPE_KEY",
        presentIn: ["dev", "staging"],
        dismissedAt: "2026-09-06T09:00:00+00:00",
      },
    });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    await userEvent.click(await screen.findByRole("button", { name: "Not here" }));

    await userEvent.click(await screen.findByRole("button", { name: "1 dismissed" }));

    expect(await screen.findByRole("button", { name: "Mention again" })).toBeInTheDocument();
  });

  /**
   * An aside on a screen that works without it. A token that cannot see the
   * other environments has no notice rather than a red box where one would be.
   */
  it("says nothing at all when the instance refuses it", async () => {
    installInstance({ [secrets]: [key] });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    expect(await screen.findByRole("link", { name: "DATABASE_URL" })).toBeInTheDocument();
    expect(
      screen.queryByRole("region", { name: "Keys missing in this environment" }),
    ).not.toBeInTheDocument();
  });

  it("says nothing when nothing is missing", async () => {
    installInstance({ [secrets]: [key], [missing]: [] });

    renderUnderShell("/projects/landing-page/prod", <Shell />);

    expect(await screen.findByRole("link", { name: "DATABASE_URL" })).toBeInTheDocument();
    expect(
      screen.queryByRole("region", { name: "Keys missing in this environment" }),
    ).not.toBeInTheDocument();
  });
});
