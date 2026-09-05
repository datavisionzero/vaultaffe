import { useEffect, useState } from "react";
import { Navigate, Route, Routes } from "react-router";
import { api, type Instance } from "@/api/client";
import { Doorstep } from "./Doorstep";
import { FirstRun } from "./FirstRun";
import { Invitation } from "./Invitation";
import { SignIn } from "./SignIn";

/**
 * What a tab that is nobody can reach: the first run, signing in, and accepting
 * an invitation.
 *
 * Which of them applies is the instance's own answer rather than a guess, so
 * the door asks it once — `GET /api/v1/instance` needs no credential precisely
 * because a client asking it has none yet. An installation that has not been
 * started has **only** the first run, at `/start`, and every other address
 * leads there; there is nothing else to reach and nothing to come back to.
 *
 * Once it has been started, `/invite` is its own address because the link an
 * administrator hands over has to lead somewhere by itself; everything else is
 * the sign-in screen **at whatever address was asked for**, so that signing in
 * renders the shell over the same URL and a link into an environment arrives
 * there.
 *
 * `/device` is not here and is not a route of this application at all: it is the
 * instance's own page, it holds no session and it asks for a password every time
 * ([ADR 0008](../../../../docs/adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)).
 */
type Standing = { at: "asking" } | { at: "answered"; instance?: Instance };

export function Doorway({ onSignedIn }: { onSignedIn: (token: string) => void }) {
  const [standing, setStanding] = useState<Standing>({ at: "asking" });

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data } = await api.GET("/api/v1/instance");

        if (current) {
          setStanding({ at: "answered", instance: data });
        }
      } catch {
        // An instance that did not answer is not an instance that needs
        // starting. The sign-in screen is what a stranger gets, and it says
        // for itself when the instance is unreachable.
        if (current) {
          setStanding({ at: "answered" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, []);

  if (standing.at === "asking") {
    return (
      <Doorstep>
        <p role="status" className="text-sm text-muted-foreground">
          Asking this instance who it is…
        </p>
      </Doorstep>
    );
  }

  const { instance } = standing;

  if (instance?.started === false) {
    return (
      <Routes>
        <Route path="/start" element={<FirstRun onSignedIn={onSignedIn} />} />
        <Route path="*" element={<Navigate to="/start" replace />} />
      </Routes>
    );
  }

  return (
    <Routes>
      <Route path="/invite" element={<Invitation onSignedIn={onSignedIn} />} />
      <Route
        path="*"
        element={
          <SignIn organization={instance?.organizationName ?? undefined} onSignedIn={onSignedIn} />
        }
      />
    </Routes>
  );
}
