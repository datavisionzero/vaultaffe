import { FolderIcon, HistoryIcon, type LucideIcon } from "lucide-react";

/**
 * The screens the navigation leads to, and the addresses of the things inside
 * them. `docs/human-interface.md` holds the matrix this is the code of; a row
 * added there is added here, and a row that exists only here is a screen the
 * document never agreed to.
 *
 * The frame carries the two top-level places and the settings area. Anything
 * below a project is addressed rather than navigated to: a project, an
 * environment and a key are links somebody can read and retype, which is why
 * the paths are built here rather than interpolated at a call site.
 */
export type View = {
  id: string;
  label: string;
  path: string;
  icon: LucideIcon;
  /** What the screen is for, in the sentence the palette shows beside it. */
  hint: string;
};

export const views: View[] = [
  {
    id: "projects",
    label: "Projects",
    path: "/projects",
    icon: FolderIcon,
    hint: "Every project of the organization, with its environments.",
  },
  {
    id: "changes",
    label: "Change log",
    path: "/changes",
    icon: HistoryIcon,
    hint: "What was changed, by whom, and by what kind of thing.",
  },
];

/**
 * The settings area: a list of areas beside the area, each with an address of
 * its own, which is the pattern the sister project's administrative screens
 * settled on. `/settings` alone is the first of them.
 */
export type Area = {
  id: string;
  label: string;
  path: string;
  hint: string;
};

export const areas: Area[] = [
  {
    id: "tokens",
    label: "Tokens",
    path: "/settings/tokens",
    hint: "The organization's tokens: kind, scopes, binding and standing.",
  },
  {
    id: "users",
    label: "Users",
    path: "/settings/users",
    hint: "The people of the organization, and the link that invites one.",
  },
  {
    id: "organization",
    label: "Organization",
    path: "/settings/organization",
    hint: "The organization's name.",
  },
  {
    id: "profile",
    label: "Profile",
    path: "/settings/profile",
    hint: "Your own name and password.",
  },
];

/**
 * Where "Settings" leads: the first area of the list, because `/settings` on
 * its own would be a screen with nothing on it.
 */
export const settingsPath = areas[0].path;

/**
 * A project and an environment are named in lower case and a key in upper, and
 * the address keeps that difference because the domain makes it
 * ([ADR 0003](../../../../docs/adr/0003-a-name-inside-a-reference-is-narrow-and-lower-case.md)).
 * Each segment is escaped: a name the instance accepted is still the author's
 * word rather than a generated one.
 */
export function projectPath(project: string): string {
  return `/projects/${encodeURIComponent(project)}`;
}

export function environmentPath(project: string, environment: string): string {
  return `${projectPath(project)}/${encodeURIComponent(environment)}`;
}

export function secretPath(project: string, environment: string, name: string): string {
  return `${environmentPath(project, environment)}/${encodeURIComponent(name)}`;
}

/**
 * The page a human confirms a device-code login on. It is rendered by the
 * instance, holds no session and is not part of this application
 * ([ADR 0008](../../../../docs/adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)),
 * so it is reached by leaving rather than by routing — a `<Link>` to it would
 * hand a page this application does not have to the router.
 */
export const devicePath = "/device";
