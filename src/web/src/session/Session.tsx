import type { ReactNode } from "react";
import type { Me } from "@/api/client";
import { SessionContext } from "./context";

/**
 * Who is signed in, for every screen under the shell. The value is what
 * `GET /api/v1/me` answered on load; a screen that needs the caller reads it
 * here rather than asking again.
 *
 * A person's session reaches the whole organization with every scope
 * (`docs/api.md`), so nothing here is a permission to draw a screen from. The
 * one line that exists is `isAdministrator`, and even that disables a control
 * with its reason beside it rather than hiding it
 * (`docs/human-interface.md`).
 */
export type Session = {
  me: Me;
  signOut: () => void;
  /**
   * What the instance last said about the caller, when a screen has changed it.
   * Told rather than re-asked: the answer to the act carries the new value, and
   * asking again would be a second request to learn what the first one already
   * said.
   */
  remember: (me: Me) => void;
};

export function SessionProvider({ value, children }: { value: Session; children: ReactNode }) {
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}
