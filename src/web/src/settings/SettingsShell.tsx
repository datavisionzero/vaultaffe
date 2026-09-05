import { NavLink, Outlet } from "react-router";
import { PageHeader } from "@/shared/PageHeader";
import { areas } from "@/shell/views";
import { cn } from "@/lib/utils";

/**
 * The settings area: the list of areas beside the area, each with an address of
 * its own, so that a link to "Tokens" is a link to what somebody was looking at
 * (`docs/human-interface.md`).
 *
 * On a narrow screen the list folds above the area rather than disappearing
 * into a menu — four entries fit, and a phone performs the same actions as a
 * desktop does.
 */
export function SettingsShell() {
  return (
    <>
      <PageHeader title="Settings" />
      <div className="flex flex-1 flex-col gap-4 p-4 md:flex-row md:gap-6">
        <nav aria-label="Settings" className="md:w-48 md:shrink-0">
          <ul className="flex flex-wrap gap-1 md:flex-col">
            {areas.map((area) => (
              <li key={area.id}>
                <NavLink
                  to={area.path}
                  className={({ isActive }) =>
                    cn(
                      "block rounded-md px-2.5 py-1.5 text-sm",
                      isActive ? "bg-accent font-medium text-accent-foreground" : "text-muted-foreground hover:bg-accent/60",
                    )
                  }
                >
                  {area.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
        <div className="flex min-w-0 flex-1 flex-col">
          <Outlet />
        </div>
      </div>
    </>
  );
}
