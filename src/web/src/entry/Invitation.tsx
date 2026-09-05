import { useEffect, useState, type FormEvent } from "react";
import { useLocation } from "react-router";
import { api, describe, type InvitationOffer } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Field, Refusal } from "@/shared/Form";
import { Doorstep } from "./Doorstep";

/**
 * `/invite` — where somebody an administrator invited becomes a person of the
 * organization.
 *
 * **The code is in the fragment of the link and stays out of every request line**
 * ([ADR 0015](../../../../docs/adr/0015-an-invitation-is-a-credential-in-a-link.md)):
 * a fragment never leaves the browser, and the two endpoints this screen calls
 * take it in a body. So this screen reads `location.hash`, and nothing here ever
 * puts the code into a path, a query or `sessionStorage`.
 *
 * Accepting is also signing in — the answer carries a session, exactly as the
 * first run's does — so a new person lands in the application rather than at a
 * sign-in form asking for the password they just chose.
 */
type Standing =
  | { at: "asking" }
  | { at: "unknown"; why: string }
  | { at: "offered"; offer: InvitationOffer };

export function Invitation({ onSignedIn }: { onSignedIn: (token: string) => void }) {
  const { hash } = useLocation();
  const code = hash.startsWith("#") ? hash.slice(1) : "";

  const [standing, setStanding] = useState<Standing>({ at: "asking" });
  const [name, setName] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();

  useEffect(() => {
    let current = true;

    void (async () => {
      if (code === "") {
        setStanding({
          at: "unknown",
          why: "This address needs the whole link an administrator handed over, including the part after the #.",
        });

        return;
      }

      try {
        const { data, error, response } = await api.POST("/api/v1/invitations/offer", {
          body: { code },
        });

        if (!current) {
          return;
        }

        if (data === undefined) {
          setStanding({ at: "unknown", why: describe(error, response.status) });
          return;
        }

        setStanding({ at: "offered", offer: data });
        setName(data.name);
      } catch {
        if (current) {
          setStanding({ at: "unknown", why: "The instance did not answer." });
        }
      }
    })();

    return () => {
      current = false;
    };
  }, [code]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST("/api/v1/invitations/acceptance", {
        body: { code, name, password },
      });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      onSignedIn(data.token);
    } catch {
      setRefusal("The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  if (standing.at === "asking") {
    return (
      <Doorstep title="An invitation">
        <p role="status" className="text-sm text-muted-foreground">
          Reading what this link is for…
        </p>
      </Doorstep>
    );
  }

  if (standing.at === "unknown") {
    return (
      <Doorstep title="This link leads nowhere">
        <p className="text-sm text-muted-foreground">{standing.why}</p>
      </Doorstep>
    );
  }

  const { offer } = standing;

  // An invitation is spendable once and stops working after three days, so the
  // three states that are not `open` are each a sentence saying which — the
  // person holding it is the one it was written for, and knowing which of them
  // it is, is what tells them to ask for another.
  if (offer.state !== "open") {
    return (
      <Doorstep title="An invitation" meta={`to ${offer.organizationName}`}>
        <p className="text-sm text-muted-foreground">
          {offer.state === "accepted" &&
            "This invitation has already been used. If that was you, sign in; if it was not, ask an administrator for a new one."}
          {offer.state === "withdrawn" &&
            "An administrator took this invitation back. They can write out a new one."}
          {offer.state === "expired" &&
            "This invitation ran out. They are good for three days; an administrator writes out a new one."}
        </p>
      </Doorstep>
    );
  }

  return (
    <Doorstep title="Join" meta={offer.organizationName}>
      <form className="grid gap-4" onSubmit={(event) => void submit(event)}>
        {/* The address is the invitation's and not this form's: an invitation to
            one address that created a person at another would not be one. */}
        <Field label="Email" value={offer.email} readOnly disabled hint="The address you were invited at." />
        <Field
          label="Your name"
          autoFocus
          required
          maxLength={100}
          value={name}
          onChange={(event) => setName(event.target.value)}
          hint="What the change log will call you."
        />
        <Field
          label="Password"
          type="password"
          autoComplete="new-password"
          required
          minLength={12}
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          hint="At least twelve characters. Length is the only rule."
        />

        {refusal !== undefined && <Refusal>{refusal}</Refusal>}

        <Button type="submit" disabled={busy || name.trim() === "" || password.length < 12}>
          {busy ? "Joining…" : "Join the organization"}
        </Button>
      </form>
    </Doorstep>
  );
}
