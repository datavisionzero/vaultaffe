import { useState, type FormEvent } from "react";
import { api, describe } from "@/api/client";
import { Button } from "@/components/ui/button";
import { useSession } from "@/session/useSession";
import { Done, Field, Refusal } from "@/shared/Form";

/**
 * `/settings/profile` — your own name, the address you sign in with, and your
 * own password (`docs/human-interface.md`).
 *
 * Three forms, because they are three acts with different consequences: a name
 * is what the change log will call you from now on, an address is what you type
 * to sign in, and a password change **ends every other session of yours**. Each
 * says which of those it is before it is done rather than afterwards, and the
 * instance is what actually does it.
 *
 * Two of them ask for the password you already have, and for one reason: a
 * session left open on a borrowed machine should not be enough to lock its owner
 * out. Changing a password can do that, and so can taking away the address its
 * owner signs in with.
 *
 * There is no "forgot my password" anywhere in this application, and there is
 * nothing missing: the instance sends no email, so somebody who cannot sign in
 * asks an administrator, who sets one and hands it over
 * ([Specification §6.1](../../../../Specification.md#61-web-ui)).
 */
export function Profile() {
  const { me } = useSession();

  return (
    <div className="grid max-w-lg gap-8">
      <section aria-label="You" className="grid gap-3">
        <div>
          <h2 className="text-sm font-semibold">You</h2>
          <p className="text-xs text-muted-foreground">
            <span className="font-mono">{me.name}</span> ·{" "}
            {me.isAdministrator ? "Administrator" : "Member of the organization"}
          </p>
        </div>
        <Name />
      </section>

      <section aria-label="The address you sign in with" className="grid gap-3">
        <div>
          <h2 className="text-sm font-semibold">The address you sign in with</h2>
          <p className="text-xs text-muted-foreground">
            <span className="font-mono">{me.email}</span> — not somewhere this instance sends
            anything, because it sends nothing. Changing it changes what you type to sign in, and
            leaves every session and token of yours working.
          </p>
        </div>
        <Address />
      </section>

      <section aria-label="Password" className="grid gap-3">
        <div>
          <h2 className="text-sm font-semibold">Password</h2>
          <p className="text-xs text-muted-foreground">
            Changing it ends every other session you are signed in with, and leaves this one alone.
            Your service and agent tokens never depended on it and go on working.
          </p>
        </div>
        <Password />
      </section>
    </div>
  );
}

function Name() {
  const { me, remember } = useSession();
  const [name, setName] = useState(me.name);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [saved, setSaved] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);
    setSaved(false);

    try {
      const { data, error, response } = await api.PATCH("/api/v1/me", { body: { name } });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      // The frame shows this name in the account menu, so the tab is told rather
      // than left showing the old one until something reloads it.
      remember({ ...me, name: data.name });
      setSaved(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="grid gap-3 rounded-lg border p-4" onSubmit={(event) => void submit(event)}>
      <Field
        label="Your name"
        required
        maxLength={100}
        value={name}
        onChange={(event) => setName(event.target.value)}
        hint="What the change log calls you. It is not what you sign in with — that is the address below."
      />

      {refusal !== undefined && <Refusal>{refusal}</Refusal>}
      {saved && <Done>Saved.</Done>}

      <div>
        <Button type="submit" disabled={busy || name.trim() === "" || name === me.name}>
          {busy ? "Saving…" : "Save"}
        </Button>
      </div>
    </form>
  );
}

/**
 * The address, and the password that has to come with it.
 *
 * An administrator can do this for anybody, which is how a lock-out is repaired;
 * this is the way that needs nobody. What it does not do is end any session —
 * an address is not a credential, and nothing was compromised by changing one.
 */
function Address() {
  const { me, remember } = useSession();
  const [email, setEmail] = useState(me.email);
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [changed, setChanged] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);
    setChanged(false);

    try {
      const { data, error, response } = await api.POST("/api/v1/me/email", {
        body: { email, currentPassword: password },
      });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      // The address the instance kept, in the one spelling it holds — not the
      // one that was typed.
      setEmail(data.email);
      remember({ ...me, email: data.email });
      setPassword("");
      setChanged(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="grid gap-3 rounded-lg border p-4" onSubmit={(event) => void submit(event)}>
      <Field
        label="The address you will sign in with"
        type="email"
        autoComplete="off"
        required
        value={email}
        onChange={(event) => setEmail(event.target.value)}
        hint="One spelling is kept: an address is stored and looked up in lower case."
      />
      <Field
        label="Your current password"
        type="password"
        autoComplete="current-password"
        required
        value={password}
        onChange={(event) => setPassword(event.target.value)}
        hint="Asked for so that a session left open somewhere is not enough to take your sign-in away."
      />

      {refusal !== undefined && <Refusal>{refusal}</Refusal>}
      {changed && <Done>Changed. Sign in with it from now on; your sessions all stay.</Done>}

      <div>
        <Button
          type="submit"
          disabled={busy || password === "" || email.trim() === "" || email.trim() === me.email}
        >
          {busy ? "Changing…" : "Change it"}
        </Button>
      </div>
    </form>
  );
}

function Password() {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [changed, setChanged] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);
    setChanged(false);

    try {
      const { error, response } = await api.POST("/api/v1/me/password", {
        body: { currentPassword: current, newPassword: next },
      });

      if (response.status >= 400) {
        setRefusal(describe(error, response.status));
        return;
      }

      // Nothing about either password stays in this tab once it has been sent.
      setCurrent("");
      setNext("");
      setChanged(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="grid gap-3 rounded-lg border p-4" onSubmit={(event) => void submit(event)}>
      <Field
        label="Your current password"
        type="password"
        autoComplete="current-password"
        required
        value={current}
        onChange={(event) => setCurrent(event.target.value)}
        hint="Asked for so that a session left open somewhere is not enough to lock you out of your own account."
      />
      <Field
        label="Your new password"
        type="password"
        autoComplete="new-password"
        required
        minLength={12}
        value={next}
        onChange={(event) => setNext(event.target.value)}
        hint="At least twelve characters. Length is the only rule there is."
      />

      {refusal !== undefined && <Refusal>{refusal}</Refusal>}
      {changed && <Done>Changed. Your other sessions have ended.</Done>}

      <div>
        <Button type="submit" disabled={busy || current === "" || next.length < 12}>
          {busy ? "Changing…" : "Change it"}
        </Button>
      </div>
    </form>
  );
}
