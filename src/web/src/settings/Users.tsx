import { MoreHorizontalIcon, UserPlusIcon } from "lucide-react";
import { useState, type FormEvent } from "react";
import { answered, api, describe, type Invitation, type User } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { useSession } from "@/session/useSession";
import { ActionDialog } from "@/shared/ActionDialog";
import { Copyable } from "@/shared/Copyable";
import { Field, Refusal } from "@/shared/Form";
import { around, when } from "@/shared/moments";
import { Rows } from "@/shared/Rows";

/**
 * `/settings/users` — the people of the organization, the invitation link to
 * copy, and an administrator's password reset (`docs/human-interface.md`).
 *
 * **The instance sends no email**, and this screen says so rather than leaving a
 * reader hunting for the mail that never comes: an invitation is a link
 * somebody copies and hands over, a reset is a password an administrator sets
 * and hands over. That is the price of having no external dependency to operate
 * ([Specification §4](../../../../Specification.md#4-guiding-principles)).
 *
 * Every act here is an administrator's. A person who is not one **sees the
 * controls disabled with the reason beside them** rather than not at all: a
 * control that vanishes is a question nobody can ask, and hiding one is never
 * the authorization check — the instance refuses, and this screen shows what it
 * said.
 */
export function Users() {
  const { me } = useSession();
  const [people, askPeopleAgain] = useAsk<User[]>("users", () => api.GET("/api/v1/users"));

  return (
    <div className="grid gap-8">
      <section className="grid gap-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="text-sm font-semibold">People</h2>
            <p className="text-xs text-muted-foreground">
              Everybody here sees and changes everything in this organization. The one line is who
              administers it.
            </p>
          </div>
          <InviteDialog onInvited={askPeopleAgain} />
        </div>

        {people.at === "asking" && <Rows />}
        {people.at === "refused" && <Refusal>{people.why}</Refusal>}
        {people.at === "answered" && (
          <ul className="divide-y rounded-lg border">
            {people.data.map((person) => (
              <Person key={person.id} person={person} onChanged={askPeopleAgain} />
            ))}
          </ul>
        )}
      </section>

      {me.isAdministrator ? (
        <Invitations />
      ) : (
        <section className="grid gap-1">
          <h2 className="text-sm font-semibold">Invitations</h2>
          <p className="text-xs text-muted-foreground">
            Who is invited into this organization is an administrator's to see and to decide. Ask
            one.
          </p>
        </section>
      )}
    </div>
  );
}

/** One person, and the acts on them — in this row's own menu. */
function Person({ person, onChanged }: { person: User; onChanged: () => void }) {
  const { me } = useSession();
  const [resetting, setResetting] = useState(false);
  const [deactivating, setDeactivating] = useState(false);

  const yourself = person.id === me.userId;

  // The reason a control is disabled, or nothing when it is not. Both reasons
  // are the instance's own rules said before it has to say them.
  const notAnAdministrator = me.isAdministrator
    ? undefined
    : "Administering the organization and its people is an administrator's.";

  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2.5">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline gap-x-2">
          <span className="text-sm font-medium">{person.name}</span>
          <span className="text-xs text-muted-foreground">
            {person.isAdministrator ? "Administrator" : "Member of the organization"}
          </span>
          {yourself && <span className="text-xs text-muted-foreground">· you</span>}
        </div>
        <div className="flex flex-wrap items-baseline gap-x-2 font-mono text-xs text-muted-foreground">
          <span className="truncate">{person.email}</span>
          {/* Status is a word, never a colour alone (`docs/human-interface.md`). */}
          {person.deactivatedAt !== null && (
            <span className="text-destructive" title={when(person.deactivatedAt)}>
              deactivated {around(person.deactivatedAt)}
            </span>
          )}
        </div>
      </div>

      <DropdownMenu>
        <DropdownMenuTrigger
          render={<Button variant="ghost" size="icon-sm" aria-label={`Actions for ${person.name}`} />}
        >
          <MoreHorizontalIcon />
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="min-w-56">
          <Act
            label="Reset their password…"
            reason={notAnAdministrator}
            onClick={() => setResetting(true)}
          />
          {person.deactivatedAt === null ? (
            <Act
              label="Deactivate…"
              reason={
                notAnAdministrator ??
                (yourself
                  ? "Nobody deactivates themselves: it is the one way to leave an instance nothing can administer."
                  : undefined)
              }
              onClick={() => setDeactivating(true)}
            />
          ) : (
            <Act
              label="Put them back"
              reason={notAnAdministrator}
              onClick={() => {
                void answered(
                  api.POST("/api/v1/users/{id}/reactivate", {
                    params: { path: { id: person.id } },
                  }),
                ).then(onChanged);
              }}
            />
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <ResetDialog
        person={person}
        open={resetting}
        onOpenChange={setResetting}
        onChanged={onChanged}
      />

      <ActionDialog
        open={deactivating}
        onOpenChange={setDeactivating}
        title={`Deactivate ${person.name}?`}
        description={
          "They stop being able to sign in, and every token of theirs stops working the same "
          + "minute — their sessions, and the agent tokens they are accountable for. Nothing they "
          + "changed is removed: the change log keeps its author. An administrator can put them "
          + "back."
        }
        confirmLabel="Deactivate"
        onConfirm={async () => {
          await answered(
            api.POST("/api/v1/users/{id}/deactivate", { params: { path: { id: person.id } } }),
          );

          onChanged();
        }}
      />
    </li>
  );
}

/**
 * One act in a row's menu — or the same act, disabled, with the reason beside
 * it. `docs/human-interface.md`: a control that vanishes is a question nobody
 * can ask.
 */
function Act({ label, reason, onClick }: { label: string; reason?: string; onClick: () => void }) {
  if (reason === undefined) {
    return <DropdownMenuItem onClick={onClick}>{label}</DropdownMenuItem>;
  }

  return (
    <div className="px-2 py-1.5">
      <div className="text-sm text-muted-foreground opacity-60">{label}</div>
      <p className="mt-0.5 text-xs text-muted-foreground">{reason}</p>
    </div>
  );
}

/** An administrator sets a password, and hands it over themselves. */
function ResetDialog({
  person,
  open,
  onOpenChange,
  onChanged,
}: {
  person: User;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onChanged: () => void;
}) {
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST("/api/v1/users/{id}/password", {
        params: { path: { id: person.id } },
        body: { password },
      });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      setPassword("");
      onOpenChange(false);
      onChanged();
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <form className="grid gap-4" onSubmit={(event) => void submit(event)}>
          <DialogHeader>
            <DialogTitle>Reset the password of {person.name}</DialogTitle>
            <DialogDescription>
              This instance sends no email, so you set a password and hand it over yourself. Every
              session they are signed in with ends now; their service and agent tokens keep working.
            </DialogDescription>
          </DialogHeader>
          <Field
            label="Their new password"
            type="password"
            autoComplete="new-password"
            required
            minLength={12}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            hint="At least twelve characters. Length is the only rule."
          />
          {refusal !== undefined && <Refusal>{refusal}</Refusal>}
          <DialogFooter>
            <Button variant="outline" disabled={busy} onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={busy || password.length < 12}>
              {busy ? "Setting…" : "Set it"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Writing out an invitation, and the one screen its link ever appears on.
 *
 * The answer carries the code once and no listing carries it afterwards
 * ([ADR 0015](../../../../docs/adr/0015-an-invitation-is-a-credential-in-a-link.md)),
 * so this dialog says that before it is closed — the same sentence a token's
 * value gets, for the same reason.
 */
function InviteDialog({ onInvited }: { onInvited: () => void }) {
  const { me } = useSession();
  const [open, setOpen] = useState(false);
  const [email, setEmail] = useState("");
  const [name, setName] = useState("");
  const [administrator, setAdministrator] = useState(false);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string>();
  const [link, setLink] = useState<string>();

  function change(next: boolean) {
    setOpen(next);

    if (!next) {
      setEmail("");
      setName("");
      setAdministrator(false);
      setRefusal(undefined);
      setLink(undefined);
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setRefusal(undefined);

    try {
      const { data, error, response } = await api.POST("/api/v1/invitations", {
        body: { email, name, isAdministrator: administrator },
      });

      if (data === undefined) {
        setRefusal(describe(error, response.status));
        return;
      }

      // Absolute here rather than at the instance: it answers a relative link
      // because it does not know what address a browser reached it at, and this
      // application is the one thing that does.
      setLink(new URL(data.link, window.location.origin).toString());
      onInvited();
    } finally {
      setBusy(false);
    }
  }

  if (!me.isAdministrator) {
    return (
      <div className="text-xs text-muted-foreground">
        Inviting somebody is an administrator's. Ask one.
      </div>
    );
  }

  return (
    <>
      <Button onClick={() => change(true)}>
        <UserPlusIcon />
        Invite somebody
      </Button>

      <Dialog open={open} onOpenChange={change}>
        <DialogContent>
          {link === undefined ? (
            <form className="grid gap-4" onSubmit={(event) => void submit(event)}>
              <DialogHeader>
                <DialogTitle>Invite somebody</DialogTitle>
                <DialogDescription>
                  This instance sends no email. What comes back is a link you copy and hand over —
                  it is good for three days and works once.
                </DialogDescription>
              </DialogHeader>
              <Field
                label="Email"
                type="email"
                autoFocus
                required
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                hint="The address they will sign in with."
              />
              <Field
                label="Name"
                required
                maxLength={100}
                value={name}
                onChange={(event) => setName(event.target.value)}
                hint="They can correct it when they accept."
              />
              <label className="flex items-start gap-2 text-sm">
                <input
                  type="checkbox"
                  name="isAdministrator"
                  className="mt-0.5 size-4 accent-primary"
                  checked={administrator}
                  onChange={(event) => setAdministrator(event.target.checked)}
                />
                <span>
                  They administer the organization
                  <span className="block text-xs text-muted-foreground">
                    Everybody here sees and changes everything. An administrator also invites
                    people, resets passwords and renames the organization.
                  </span>
                </span>
              </label>
              {refusal !== undefined && <Refusal>{refusal}</Refusal>}
              <DialogFooter>
                <Button variant="outline" disabled={busy} onClick={() => change(false)}>
                  Cancel
                </Button>
                <Button type="submit" disabled={busy || email === "" || name.trim() === ""}>
                  {busy ? "Writing…" : "Write the invitation"}
                </Button>
              </DialogFooter>
            </form>
          ) : (
            <div className="grid gap-4">
              <DialogHeader>
                <DialogTitle>The link for {name}</DialogTitle>
                <DialogDescription>
                  <strong>This is the once.</strong> The link is not stored anywhere it could be read
                  back, and no listing will show it again. Copy it now and hand it over; if it is
                  lost, withdraw the invitation and write another.
                </DialogDescription>
              </DialogHeader>
              <Copyable value={link} label="Invitation link" />
              <DialogFooter>
                <Button onClick={() => change(false)}>Done</Button>
              </DialogFooter>
            </div>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
}

/** Every invitation of the organization, and the one act on an open one. */
function Invitations() {
  const [invitations, again] = useAsk<Invitation[]>("invitations", () => api.GET("/api/v1/invitations"));

  return (
    <section className="grid gap-3">
      <div>
        <h2 className="text-sm font-semibold">Invitations</h2>
        <p className="text-xs text-muted-foreground">
          A link works once and for three days. Withdrawing one stops it working; the row stays, so
          it still says who invited whom.
        </p>
      </div>

      {invitations.at === "asking" && <Rows />}
      {invitations.at === "refused" && <Refusal>{invitations.why}</Refusal>}
      {invitations.at === "answered" &&
        (invitations.data.length === 0 ? (
          <p className="rounded-lg border border-dashed px-3 py-6 text-center text-sm text-muted-foreground">
            Nobody has been invited yet.
          </p>
        ) : (
          <ul className="divide-y rounded-lg border">
            {invitations.data.map((invitation) => (
              <li
                key={invitation.id}
                className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2.5"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-baseline gap-x-2">
                    <span className="text-sm font-medium">{invitation.name}</span>
                    {invitation.isAdministrator && (
                      <span className="text-xs text-muted-foreground">as an administrator</span>
                    )}
                  </div>
                  <div className="flex flex-wrap items-baseline gap-x-2 text-xs text-muted-foreground">
                    <span className="truncate font-mono">{invitation.email}</span>
                    <span>· {invitation.state}</span>
                    {invitation.state === "open" && (
                      <span title={when(invitation.expiresAt)}>
                        · runs out {around(invitation.expiresAt)}
                      </span>
                    )}
                  </div>
                </div>
                {invitation.state === "open" && (
                  <ActionDialog
                    trigger={
                      <Button variant="outline" size="sm">
                        Withdraw
                      </Button>
                    }
                    title={`Withdraw the invitation for ${invitation.email}?`}
                    description="The link stops working immediately. Whoever was given it can no longer join with it, and you can write out another."
                    confirmLabel="Withdraw"
                    onConfirm={async () => {
                      await answered(
                        api.DELETE("/api/v1/invitations/{id}", {
                          params: { path: { id: invitation.id } },
                        }),
                      );

                      again();
                    }}
                  />
                )}
              </li>
            ))}
          </ul>
        ))}
    </section>
  );
}
