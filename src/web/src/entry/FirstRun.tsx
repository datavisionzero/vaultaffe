import { useState, type FormEvent } from "react";
import { api, describe } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Field, Refusal } from "@/shared/Form";
import { Doorstep } from "./Doorstep";

/**
 * `/start` — the first run: the default organization comes into being and the
 * person filling this in becomes its administrator
 * ([Specification §6.3](../../../../Specification.md#63-operations)).
 *
 * It is **the only screen an unstarted instance has**, which is why the door
 * leads here rather than offering it: there is nobody to sign in as, and a
 * sign-in form in front of an empty instance reads as a password that went
 * wrong.
 *
 * It authenticates nobody, because there is nobody yet, and the second attempt
 * is refused. What it does ask for is **this instance's claim secret**, which
 * the instance writes to its own log at every start until somebody claims it
 * ([ADR 0019](../../../../docs/adr/0019-an-unclaimed-instance-holds-its-own-claim-secret.md)).
 * Whoever can read that log can start the instance; whoever merely reaches this
 * page cannot. That is what closes the window ADR 0007 left open between
 * `docker compose up` and this form.
 *
 * The field says where the secret is rather than assuming the person knows one
 * exists: they are an operator who has just brought a container up, and the
 * answer they need is a command they can paste.
 *
 * The answer carries a session, exactly as an accepted invitation's does, so
 * the first administrator lands inside rather than at a sign-in asking for the
 * password they have just chosen.
 */
export function FirstRun({ onSignedIn }: { onSignedIn: (token: string) => void }) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [claim, setClaim] = useState("");
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST("/api/v1/instance", {
        body: { email, name, password },
        // A credential and not a field of the organization being made, so it
        // travels beside the request rather than inside it.
        headers: { "Vaultaffe-Claim": claim.trim() },
      });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      onSignedIn(data.session.token);
    } catch {
      setRefusal("The instance did not answer.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Doorstep title="Start this instance" meta="It has no organization and nobody in it yet.">
      <form className="grid gap-4" onSubmit={(event) => void submit(event)}>
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
          label="Email"
          type="email"
          autoComplete="username"
          required
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          hint="How you sign in. This instance sends no mail to it."
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
        <Field
          label="Claim secret"
          // Not a password field. It is not remembered, not chosen and not
          // typed twice — it is pasted out of a log, and a person pasting one
          // has to be able to see that they pasted the whole of it.
          autoComplete="off"
          spellCheck={false}
          required
          value={claim}
          onChange={(event) => setClaim(event.target.value)}
          hint="This instance printed it in its own log. On the machine it runs on: docker compose logs vaultaffe"
        />

        {refusal !== undefined && <Refusal>{refusal}</Refusal>}

        <Button
          type="submit"
          disabled={
            busy ||
            name.trim() === "" ||
            email === "" ||
            password.length < 12 ||
            claim.trim() === ""
          }
        >
          {busy ? "Starting…" : "Start the instance"}
        </Button>

        {/* Both sentences are the operator's business rather than decoration.
            The first says why there is no invitation to wait for; the second
            says what the claim secret is protecting, which is what stops it
            reading as one more field to get past. */}
        <p className="text-xs text-muted-foreground">
          You become the administrator of the organization, which is called Default and can be
          renamed afterwards. Everybody else arrives by an invitation you hand over.
        </p>
        <p className="text-xs text-muted-foreground">
          This happens once, and the claim secret is what makes it yours: without it this page
          would hand the instance to whoever reached it first. It is printed at every start until
          somebody claims this instance, so a lost one is a restart away.
        </p>
      </form>
    </Doorstep>
  );
}
