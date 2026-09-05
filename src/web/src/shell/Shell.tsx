import { CommandIcon } from "lucide-react";
import { useEffect, useState } from "react";
import { Navigate, Route, Routes, useNavigate } from "react-router";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { Changes } from "@/changes/Changes";
import { Environment } from "@/catalogue/Environment";
import { Project } from "@/catalogue/Project";
import { Projects } from "@/catalogue/Projects";
import { Secret } from "@/catalogue/Secret";
import { SettingsShell } from "@/settings/SettingsShell";
import { Organization } from "@/settings/Organization";
import { Profile } from "@/settings/Profile";
import { Tokens } from "@/settings/Tokens";
import { Users } from "@/settings/Users";
import { AccountMenu } from "./AccountMenu";
import { AppSidebar } from "./AppSidebar";
import { Palette } from "./Palette";
import { Keys, ShortcutsDialog } from "./ShortcutsDialog";
import { is, overlaid, typing } from "./shortcuts";
import { Nowhere } from "./Nowhere";
import { settingsPath, views } from "./views";

/**
 * The frame every screen sits in: the navigation, the header, the palette and
 * the overview of the keys.
 *
 * It renders before any of the organization's data arrives, and navigation does
 * not remount it — which is what makes a loading state a skeleton inside a
 * frame rather than a blank page (`docs/human-interface.md`). Nothing here asks
 * the instance anything; the screens do that for themselves.
 *
 * `/device` is deliberately not a route. It is the instance's own page, holds
 * no session and asks for a password every time
 * ([ADR 0008](../../../../docs/adr/0008-a-session-is-a-token-and-the-only-page-asks-for-a-password.md)),
 * so it is reached by leaving this application rather than by routing inside
 * it.
 */
export function Shell() {
  const navigate = useNavigate();
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);

  // The keys the frame itself owns, read from `shortcuts.ts` so that this
  // handler and the overview it feeds cannot come apart.
  useEffect(() => {
    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (is("global:palette", event)) {
        event.preventDefault();
        // One dialog at a time: the palette arrives over whatever the overview
        // was explaining, not behind it.
        setShortcutsOpen(false);
        setPaletteOpen((open) => !open);
        return;
      }

      // Not while something is being typed — a password and a secret's value
      // are fields a bare key must reach as a letter — and not while a menu or
      // a dialog holds the keyboard; those close with Escape, as they always
      // did.
      if (typing(event) || overlaid(event)) {
        return;
      }

      if (is("global:shortcuts", event)) {
        event.preventDefault();
        setShortcutsOpen(true);
      } else if (is("global:projects", event)) {
        event.preventDefault();
        void navigate(views[0].path);
      } else if (is("global:changes", event)) {
        event.preventDefault();
        void navigate(views[1].path);
      }
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate]);

  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="flex h-12 shrink-0 items-center gap-2 border-b px-3">
          <SidebarTrigger className="md:hidden" />
          <Separator orientation="vertical" className="mr-1 h-4! md:hidden" />
          <div className="flex-1" />
          <Button
            variant="outline"
            size="sm"
            className="hidden gap-2 text-muted-foreground sm:flex"
            onClick={() => setPaletteOpen(true)}
          >
            <CommandIcon className="size-3.5" />
            <span className="text-xs">Search or jump…</span>
            <Keys id="global:palette" />
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            className="sm:hidden"
            aria-label="Command palette"
            onClick={() => setPaletteOpen(true)}
          >
            <CommandIcon />
          </Button>
          <AccountMenu onShortcuts={() => setShortcutsOpen(true)} />
        </header>

        {/* The matrix of `docs/human-interface.md`, route for route — every
            one of them now the screen it names. */}
        <Routes>
          <Route path="/" element={<Navigate to={views[0].path} replace />} />

          <Route path="/projects" element={<Projects />} />
          <Route path="/projects/:project" element={<Project />} />
          <Route path="/projects/:project/:environment" element={<Environment />} />
          <Route path="/projects/:project/:environment/:name" element={<Secret />} />
          <Route path="/changes" element={<Changes />} />

          {/* The settings area, screen by screen rather than by looping over
              the list: each of these was replaced whole by the one it names. */}
          <Route path="/settings" element={<SettingsShell />}>
            <Route index element={<Navigate to={settingsPath} replace />} />
            <Route path="tokens" element={<Tokens />} />
            <Route path="users" element={<Users />} />
            <Route path="organization" element={<Organization />} />
            <Route path="profile" element={<Profile />} />
          </Route>

          <Route path="*" element={<Nowhere />} />
        </Routes>
      </SidebarInset>

      <Palette open={paletteOpen} onOpenChange={setPaletteOpen} onShortcuts={() => setShortcutsOpen(true)} />
      <ShortcutsDialog open={shortcutsOpen} onOpenChange={setShortcutsOpen} />
    </SidebarProvider>
  );
}

/**
 * What the frame shows while a screen or the answer behind it is still on its
 * way. Never a blank page, which `docs/human-interface.md` asks for, and never
 * silent to a screen reader.
 */
export function Busy({ title }: { title: string }) {
  return (
    <div aria-busy className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
      <span aria-hidden className="size-4.5 animate-pulse rounded-sm bg-brand" />
      <p role="status" className="text-sm text-muted-foreground">
        {title}
      </p>
    </div>
  );
}

export function Empty({ title, children }: { title: string; children?: React.ReactNode }) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
      <p className="font-medium">{title}</p>
      {children !== undefined && <p className="text-sm text-muted-foreground">{children}</p>}
    </div>
  );
}
