import { useEffect, useState } from "react";
import { api, whenSignedOut, type Me } from "@/api/client";
import { SessionProvider } from "@/session/Session";
import { dropToken } from "@/session/token";
import { Shell } from "@/shell/Shell";

/**
 * Whether this tab is somebody, which decides the first screen.
 *
 * Every load asks the instance who the token in this tab belongs to (ADR 0014).
 * The shell appears once that answers; an absent, expired or revoked session is
 * a stranger and gets the sign-in screen.
 */
type Standing =
  | { at: "asking" }
  | { at: "unreachable" }
  | { at: "stranger" }
  | { at: "known"; me: Me };

export function App() {
  const [standing, setStanding] = useState<Standing>({ at: "asking" });

  useEffect(() => {
    if (standing.at !== "asking") {
      return;
    }

    let current = true;

    void (async () => {
      try {
        const { data, response } = await api.GET("/api/v1/me");

        if (!current) {
          return;
        }

        if (data !== undefined) {
          setStanding({ at: "known", me: data });
        } else if (response.status === 401) {
          // Whatever this tab was carrying does not authenticate. Which of the
          // four reasons it is, is information about a credential the caller
          // does not hold (`docs/api.md`) — so the token goes, and the reader
          // gets one sentence.
          dropToken();
          setStanding({ at: "stranger" });
        } else {
          setStanding({ at: "unreachable" });
        }
      } catch {
        if (current) {
          setStanding({ at: "unreachable" });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [standing.at]);

  useEffect(
    () =>
      whenSignedOut(() => {
        setStanding({ at: "stranger" });
      }),
    [],
  );

  switch (standing.at) {
    case "asking":
      // Not a blank page: the frame of the sign-in screen is already the truth,
      // and only the sentence under it is waiting.
      return (
        <main aria-busy className="flex min-h-svh flex-col items-center justify-center gap-3 p-6 text-center">
          <div className="flex items-center gap-2 text-base font-semibold">
            <span aria-hidden className="size-4.5 animate-pulse rounded-sm bg-brand" />
            vaultaffe
          </div>
          <p role="status" className="text-sm text-muted-foreground">
            Checking your session…
          </p>
        </main>
      );

    case "unreachable":
      return (
        <main className="flex min-h-svh flex-col items-center justify-center gap-3 p-6 text-center">
          <p className="font-medium">The instance did not answer.</p>
          <button
            type="button"
            className="text-sm text-brand underline-offset-4 hover:underline"
            onClick={() => setStanding({ at: "asking" })}
          >
            Try again
          </button>
        </main>
      );

    case "stranger":
      // The first run and signing in are the screens the next piece of work
      // builds; the frame only has to know that they are where a stranger goes.
      return (
        <main className="flex min-h-svh flex-col items-center justify-center gap-3 p-6 text-center">
          <div className="flex items-center gap-2 text-base font-semibold">
            <span aria-hidden className="size-4.5 rounded-sm bg-brand" />
            vaultaffe
          </div>
          <p className="font-medium">The sign-in screen is not built yet.</p>
          <p className="max-w-prose text-sm text-muted-foreground">
            Starting an instance and signing in arrive with the screens for them. The device-code login already has
            its page: the CLI prints the address, and a person confirms it there.
          </p>
        </main>
      );

    case "known":
      return (
        <SessionProvider
          value={{
            me: standing.me,
            signOut: () => {
              // The row stays revoked rather than deleted, so the token goes on
              // authoring everything it ever changed (`docs/api.md`). This tab
              // forgets it either way — an instance that did not answer is not
              // a reason to keep a credential lying around.
              void api.DELETE("/api/v1/sessions/current").finally(() => {
                dropToken();
                setStanding({ at: "stranger" });
              });
            },
          }}
        >
          <Shell />
        </SessionProvider>
      );
  }
}
