import { NavLink, useLocation } from "react-router";
import { SettingsIcon } from "lucide-react";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/components/ui/sidebar";
import { useSession } from "@/session/useSession";
import { settingsPath, views } from "./views";

/**
 * The left navigation: the places of the organization, and the settings area
 * under them. On a phone the same component is the drawer the header button
 * opens — one application, not a reduced one
 * (`docs/human-interface.md`, the performance and accessibility floor).
 *
 * Every person of the organization sees every entry, because every person sees
 * and changes everything in it (Specification §6.4). Nothing here is hidden by
 * a permission, and nothing here is a permission check.
 */
export function AppSidebar() {
  const { me } = useSession();
  const { setOpenMobile } = useSidebar();
  const { pathname } = useLocation();

  return (
    <Sidebar collapsible="offcanvas">
      <SidebarHeader className="px-3 pt-3">
        <div className="flex items-center gap-2 px-1 text-sm font-semibold">
          <span aria-hidden className="size-4.5 rounded-sm bg-brand" />
          vaultaffe
        </div>
      </SidebarHeader>

      <SidebarContent>
        <nav aria-label="The organization">
          <SidebarGroup>
            <SidebarGroupLabel>Vault</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {views.map((view) => (
                  <SidebarMenuItem key={view.id}>
                    <SidebarMenuButton
                      isActive={pathname === view.path || pathname.startsWith(`${view.path}/`)}
                      render={<NavLink to={view.path} onClick={() => setOpenMobile(false)} />}
                    >
                      <view.icon />
                      <span>{view.label}</span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                ))}
                <SidebarMenuItem>
                  <SidebarMenuButton
                    isActive={pathname.startsWith("/settings")}
                    render={<NavLink to={settingsPath} onClick={() => setOpenMobile(false)} />}
                  >
                    <SettingsIcon />
                    <span>Settings</span>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        </nav>
      </SidebarContent>

      <SidebarFooter className="px-3 pb-3">
        <div className="truncate px-1 text-xs text-muted-foreground">{me.name}</div>
      </SidebarFooter>
    </Sidebar>
  );
}
