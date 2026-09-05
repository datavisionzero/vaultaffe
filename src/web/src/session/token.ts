/**
 * Where the browser keeps its session token (ADR 0014).
 *
 * The instance issues one credential shape for all three clients — a bearer
 * token, in the answer to a sign-in and nowhere else ever (`docs/api.md`) — so
 * there is no cookie for a browser to be handed instead, and this application
 * holds the token itself.
 *
 * It holds it in `sessionStorage`: the tab is the session's lifetime. A closed
 * tab is a signed-out browser, a second tab signs in on its own, and nothing is
 * left on the disk of a shared machine for the next person to find. The CLI's
 * rule is the same one against a different store — the keychain and nowhere
 * quietly (ADR 0012).
 */
const key = "vaultaffe.session";

/**
 * What this tab is signed in with, whether or not the browser let us write it
 * down. A private window, an embedded view or a policy that blocks site data
 * throws on the property itself rather than on the call, and an application
 * that cannot store a token can still hold one until the page is left.
 */
let held: string | undefined;

function store(): Storage | undefined {
  try {
    return window.sessionStorage;
  } catch {
    return undefined;
  }
}

export function heldToken(): string | undefined {
  if (held !== undefined) {
    return held;
  }

  try {
    held = store()?.getItem(key) ?? undefined;
  } catch {
    held = undefined;
  }

  return held;
}

export function holdToken(token: string): void {
  held = token;

  try {
    store()?.setItem(key, token);
  } catch {
    // A session that does not survive a reload is worse than one that does; it
    // is not worse than no session at all, and this tab holds it either way.
  }
}

export function dropToken(): void {
  held = undefined;

  try {
    store()?.removeItem(key);
  } catch {
    // Nothing kept it, so there is nothing to take back.
  }
}
