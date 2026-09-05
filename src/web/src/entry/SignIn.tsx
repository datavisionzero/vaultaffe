import { useEffect, useState, type FormEvent } from "react";
import { api, describe } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Field, Refusal } from "@/shared/Form";
import { Doorstep } from "./Doorstep";

/**
 * `/login` — email and password, and the sentence that says who performs a
 * reset (`docs/human-interface.md`).
 *
 * It is the screen a stranger gets at **any** address, not only at `/login`: the
 * address bar keeps whatever link they followed, and signing in renders the
 * shell over the same URL, so a bookmark into an environment lands there rather
 * than at a list they then have to walk back down. Nothing is remembered in
 * order to do that — the router never left.
 *
 * There is no "forgot my password" and this screen says why rather than leaving
 * a reader looking for one: the instance sends no email
 * ([Specification §6.1](../../../../Specification.md#61-web-ui)), so a reset is
 * something an administrator does and hands over.
 */
export function SignIn({ onSignedIn }: { onSignedIn: (token: string) => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [organization, setOrganization] = useState<{ started: boolean; name?: string }>();

  // Whose instance this is, and whether it has been started at all. A person
  // who is looking at a fresh installation should read that here rather than
  // conclude their password is wrong.
  useEffect(() => {
    let current = true;

    void (async () => {
      const { data } = await api.GET("/api/v1/instance");

      if (current && data !== undefined) {
        setOrganization({ started: data.started, name: data.organizationName ?? undefined });
      }
    })();

    return () => {
      current = false;
    };
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST("/api/v1/sessions", {
        body: { email, password },
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

  return (
    <Doorstep
      title="Sign in"
      meta={organization?.name !== undefined ? `to ${organization.name}` : undefined}
    >
      {organization?.started === false ? (
        <p className="text-sm text-muted-foreground">
          This instance has not been started yet: it has no organization and nobody to sign in as.
          Starting one is the first run, and it happens once.
        </p>
      ) : (
        <form className="grid gap-4" onSubmit={(event) => void submit(event)}>
          <Field
            label="Email"
            type="email"
            autoComplete="username"
            autoFocus
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
          <Field
            label="Password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />

          {refusal !== undefined && <Refusal>{refusal}</Refusal>}

          <Button type="submit" disabled={busy || email === "" || password === ""}>
            {busy ? "Signing in…" : "Sign in"}
          </Button>

          <p className="text-xs text-muted-foreground">
            This instance sends no email. A password reset is done by an administrator, who sets one
            and hands it over.
          </p>
        </form>
      )}
    </Doorstep>
  );
}
