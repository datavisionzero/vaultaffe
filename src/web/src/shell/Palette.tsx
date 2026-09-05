import { ArrowRightIcon, SearchIcon } from "lucide-react";
import { useId, useMemo, useState, type KeyboardEvent } from "react";
import { useNavigate } from "react-router";
import { useTheme } from "@/components/theme-provider";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useSession } from "@/session/useSession";
import { cn } from "@/lib/utils";
import { Keys } from "./ShortcutsDialog";
import { is } from "./shortcuts";
import { api, type Project } from "@/api/client";
import { useAsk } from "@/api/useAsk";
import { areas, environmentPath, projectPath, settingsPath, views } from "./views";

type Command = {
  id: string;
  label: string;
  hint?: string;
  group: string;
  run: () => void;
};

/**
 * The command palette — ⌘K, or Ctrl+K — over everywhere the frame can go and
 * the few acts it owns itself.
 *
 * It searches the names of screens and **the names of the catalogue** — the
 * projects of the organization and their environments, asked for when the
 * palette opens and not before. It will never find a value: the listing
 * endpoints do not carry values at all (`docs/api.md`), and a palette that
 * turned one up would be a reveal nobody asked for. Revealing is an act on a
 * secret, on the secret's own screen (`docs/human-interface.md`).
 *
 * Key names are deliberately not in it. Finding one would mean a listing per
 * environment on every open, and the environment screen is one keystroke away
 * from a project that is in here.
 *
 * Owned rather than imported: a filtered list with a roving index inside a Base
 * UI dialog, which is what a palette is before it does more.
 */
type PaletteProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The overview of the keys, which the palette is one of the ways to. */
  onShortcuts: () => void;
};

export function Palette({ open, onOpenChange, onShortcuts }: PaletteProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="top-[20%] translate-y-0 gap-0 overflow-hidden p-0 sm:max-w-lg" showCloseButton={false}>
        <DialogHeader className="sr-only">
          <DialogTitle>Command palette</DialogTitle>
          <DialogDescription>Search the screens of this application, and jump to one.</DialogDescription>
        </DialogHeader>
        {open && <PaletteBody onOpenChange={onOpenChange} onShortcuts={onShortcuts} />}
      </DialogContent>
    </Dialog>
  );
}

/** Mounted while the palette is open, so that its query starts empty every time. */
function PaletteBody({ onOpenChange, onShortcuts }: Omit<PaletteProps, "open">) {
  const navigate = useNavigate();
  const { setTheme } = useTheme();
  const { signOut } = useSession();
  const [query, setQuery] = useState("");
  const [index, setIndex] = useState(0);
  const searchId = useId();

  // Only while the palette is open, because this body is only mounted then: the
  // frame itself asks the instance for nothing.
  const [catalogue] = useAsk<Project[]>("palette:projects", () => api.GET("/api/v1/projects"));

  const commands = useMemo<Command[]>(() => {
    const go = (to: string) => () => {
      onOpenChange(false);
      void navigate(to);
    };

    const list: Command[] = [];

    for (const view of views) {
      list.push({ id: `view:${view.id}`, label: view.label, hint: view.hint, group: "Go to", run: go(view.path) });
    }

    list.push({ id: "view:settings", label: "Settings", group: "Go to", run: go(settingsPath) });

    for (const area of areas) {
      list.push({ id: `area:${area.id}`, label: area.label, hint: area.hint, group: "Settings", run: go(area.path) });
    }

    if (catalogue.at === "answered") {
      for (const project of catalogue.data) {
        list.push({
          id: `project:${project.id}`,
          label: project.name,
          hint: "Project",
          group: "The catalogue",
          run: go(projectPath(project.name)),
        });

        for (const environment of project.environments) {
          list.push({
            id: `environment:${environment.id}`,
            label: `${project.name}/${environment.name}`,
            hint: "Environment",
            group: "The catalogue",
            run: go(environmentPath(project.name, environment.name)),
          });
        }
      }
    }

    list.push(
      { id: "theme:light", label: "Light theme", group: "Appearance", run: () => { onOpenChange(false); setTheme("light"); } },
      { id: "theme:dark", label: "Dark theme", group: "Appearance", run: () => { onOpenChange(false); setTheme("dark"); } },
      { id: "theme:system", label: "Follow the system", group: "Appearance", run: () => { onOpenChange(false); setTheme("system"); } },
      {
        id: "shortcuts",
        label: "Keyboard shortcuts",
        hint: "Every key this application binds.",
        group: "Help",
        run: () => { onOpenChange(false); onShortcuts(); },
      },
      { id: "sign-out", label: "Sign out", group: "Account", run: () => { onOpenChange(false); signOut(); } },
    );

    return list;
  }, [catalogue, navigate, onOpenChange, onShortcuts, setTheme, signOut]);

  const matching = useMemo(() => {
    const lowered = query.trim().toLowerCase();

    if (lowered === "") {
      return commands;
    }

    return commands.filter(
      (command) =>
        command.label.toLowerCase().includes(lowered) ||
        command.hint?.toLowerCase().includes(lowered) ||
        command.group.toLowerCase().includes(lowered),
    );
  }, [commands, query]);

  const selected = matching[Math.min(index, Math.max(matching.length - 1, 0))];

  function onKeyDown(event: KeyboardEvent) {
    if (is("palette:next", event)) {
      event.preventDefault();
      setIndex((current) => Math.min(current + 1, matching.length - 1));
    } else if (is("palette:previous", event)) {
      event.preventDefault();
      setIndex((current) => Math.max(current - 1, 0));
    } else if (is("palette:run", event) && selected !== undefined) {
      event.preventDefault();
      selected.run();
    }
  }

  let lastGroup: string | undefined;

  return (
    <>
      <div className="flex items-center gap-2 border-b px-3">
        <SearchIcon className="size-4 text-muted-foreground" />
        <input
          id={searchId}
          autoFocus
          role="combobox"
          aria-expanded
          aria-controls="palette-commands"
          aria-activedescendant={selected ? `palette-${selected.id}` : undefined}
          aria-label="Search the screens, or type a command"
          placeholder="Search the screens, or type a command"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setIndex(0);
          }}
          onKeyDown={onKeyDown}
          className="h-11 flex-1 bg-transparent text-sm outline-hidden placeholder:text-muted-foreground"
        />
        <Keys id="palette:close" />
      </div>

      <ul id="palette-commands" role="listbox" className="max-h-80 overflow-y-auto p-1">
        {matching.length === 0 && (
          <li className="px-3 py-6 text-center text-sm text-muted-foreground">Nothing matches.</li>
        )}
        {matching.map((command) => {
          const heading = command.group !== lastGroup ? command.group : undefined;
          lastGroup = command.group;

          return (
            <li key={command.id} role="presentation">
              {heading !== undefined && (
                <div className="px-2 pt-2 pb-1 text-[11px] font-medium tracking-wide text-muted-foreground uppercase">
                  {heading}
                </div>
              )}
              <div
                id={`palette-${command.id}`}
                role="option"
                aria-selected={command === selected}
                onMouseMove={() => setIndex(matching.indexOf(command))}
                onClick={command.run}
                className={cn(
                  "flex cursor-default items-center gap-3 rounded-md px-2 py-1.5 text-sm",
                  command === selected && "bg-accent text-accent-foreground",
                )}
              >
                <span className="flex-1 truncate">{command.label}</span>
                {command.hint !== undefined && (
                  <span className="truncate text-xs text-muted-foreground">{command.hint}</span>
                )}
                {command === selected && <ArrowRightIcon className="size-3.5 text-muted-foreground" />}
              </div>
            </li>
          );
        })}
      </ul>
    </>
  );
}
