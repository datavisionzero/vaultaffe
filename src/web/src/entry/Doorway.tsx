import { Route, Routes } from "react-router";
import { Invitation } from "./Invitation";
import { SignIn } from "./SignIn";

/**
 * What a tab that is nobody can reach: signing in, and accepting an invitation.
 *
 * Two screens rather than a redirect to one. `/invite` is its own address
 * because the link an administrator hands over has to lead somewhere by itself;
 * everything else is the sign-in screen **at whatever address was asked for**,
 * so that signing in renders the shell over the same URL and a link into an
 * environment arrives there.
 *
 * `/device` is not here and is not a route of this application at all: it is the
 * instance's own page, it holds no session and it asks for a password every time
 * ([ADR 0008](../../../../docs/adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)).
 */
export function Doorway({ onSignedIn }: { onSignedIn: (token: string) => void }) {
  return (
    <Routes>
      <Route path="/invite" element={<Invitation onSignedIn={onSignedIn} />} />
      <Route path="*" element={<SignIn onSignedIn={onSignedIn} />} />
    </Routes>
  );
}
