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
};

export function SessionProvider({ value, children }: { value: Session; children: ReactNode }) {
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}
