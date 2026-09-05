/**
 * Every key this application binds, in one place.
 *
 * The overview the `?` opens is drawn from this list rather than written beside
 * it, so a key that is bound is a key that is advertised and the two cannot
 * come apart. `docs/human-interface.md` asks that every action be reachable by
 * keyboard; this is where the frame's own share of that is kept.
 *
 * The list holds what the frame answers, and no more. A key a screen not yet
 * built would bind is not declared here in advance — an overview promising a
 * key that does nothing is worse than one that is short — so the group the
 * forms will bring, with its Escape, arrives with the first form.
 */

/** The contexts the overview groups the keys into, in the order it shows them. */
export const groups = ["Global", "Command palette"] as const;

export type Group = (typeof groups)[number];

export type ShortcutId =
  | "global:palette"
  | "global:sidebar"
  | "global:projects"
  | "global:changes"
  | "global:shortcuts"
  | "palette:next"
  | "palette:previous"
  | "palette:run"
  | "palette:close";

export type Shortcut = {
  id: ShortcutId;
  /** The `KeyboardEvent.key` this answers to; letters are compared without case. */
  key: string;
  /** True where ⌘ — or Ctrl away from a Mac — is held. */
  mod?: boolean;
  /** What it does, as the overview says it. */
  what: string;
  group: Group;
};

/**
 * ⌘ belongs to a Mac and Ctrl to everywhere else, and a list of shortcuts that
 * names the wrong one is worse than none. Read once: the keyboard does not
 * change under a running tab.
 */
export const modLabel =
  typeof navigator !== "undefined" && /Mac|iPhone|iPad|iPod/.test(navigator.userAgent) ? "⌘" : "Ctrl";

export const shortcuts: Shortcut[] = [
  { id: "global:palette", key: "k", mod: true, what: "Search or jump to anything", group: "Global" },
  { id: "global:sidebar", key: "b", mod: true, what: "Fold the navigation", group: "Global" },
  // Bare keys, because ⌘P is the browser's print and taking printing away costs
  // more than a modifier gains. They stay out of the way of anything being
  // typed into, which is what `typing` below is for.
  { id: "global:projects", key: "p", what: "Go to the projects", group: "Global" },
  { id: "global:changes", key: "h", what: "Go to the change log", group: "Global" },
  { id: "global:shortcuts", key: "?", what: "Show this list", group: "Global" },

  { id: "palette:next", key: "ArrowDown", what: "Next result", group: "Command palette" },
  { id: "palette:previous", key: "ArrowUp", what: "Previous result", group: "Command palette" },
  { id: "palette:run", key: "Enter", what: "Go where the row leads", group: "Command palette" },
  { id: "palette:close", key: "Escape", what: "Close the palette", group: "Command palette" },
];

const index = new Map(shortcuts.map((shortcut) => [shortcut.id, shortcut] as const));

export function shortcut(id: ShortcutId): Shortcut {
  // Every id in the union is in the list above; the fallback keeps a typo in a
  // handler from taking the screen down with it.
  return index.get(id) ?? { id, key: "", what: "", group: "Global" };
}

/** As much of a key event as `is` reads — a React one and a DOM one both fit. */
type Pressed = Pick<KeyboardEvent, "key" | "altKey" | "metaKey" | "ctrlKey">;

/**
 * Whether an event is that shortcut. Shift is deliberately not compared: `?`
 * needs it on an English keyboard and a different key on a German one, and what
 * arrived is the character either way.
 */
export function is(id: ShortcutId, event: Pressed): boolean {
  const wanted = shortcut(id);

  if (wanted.key === "" || event.altKey) {
    return false;
  }

  if ((event.metaKey || event.ctrlKey) !== (wanted.mod === true)) {
    return false;
  }

  return event.key.toLowerCase() === wanted.key.toLowerCase();
}

/**
 * A bare key belongs to whatever is being typed into, and never to the frame.
 *
 * This is not only a convenience here. A password field and the field a secret
 * is written into are both fields a bare `p` must reach as a letter, and a
 * frame that navigated away mid-word would take the writing with it.
 */
export function typing(event: KeyboardEvent): boolean {
  const target = event.target as HTMLElement | null;
  return target?.matches("input, textarea, select, [contenteditable=true]") === true;
}

/** A menu or a dialog has the keyboard while it is open; those close with Escape. */
export function overlaid(event: KeyboardEvent): boolean {
  const target = event.target as HTMLElement | null;
  return target?.closest('[role="menu"], [role="dialog"]') != null;
}

const drawnKeys: Record<string, string> = {
  Enter: "Enter",
  Escape: "Esc",
  ArrowUp: "↑",
  ArrowDown: "↓",
};

/** The caps a shortcut is drawn as: `["⌘", "K"]`, `["P"]`. */
export function drawn(id: ShortcutId): string[] {
  const wanted = shortcut(id);
  const key = drawnKeys[wanted.key] ?? wanted.key.toUpperCase();

  return wanted.mod === true ? [modLabel, key] : [key];
}
