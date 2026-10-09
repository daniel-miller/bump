import {
  AppShell as Shell,
  ShellBody,
  ShellMain,
  Sidebar,
  SidebarBrand,
  SidebarNav,
  SidebarNavItem,
  SkipLink,
  Topbar,
} from "@/components/ui/app-shell";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { useAuth } from "@/hooks/useAuth";
import { useTheme } from "@/hooks/useTheme";
import { api, apiBase } from "@/lib/api";
import { useQueryClient } from "@tanstack/react-query";
import { Link, NavLink, Navigate, Outlet, useNavigate } from "react-router-dom";

const navItems = [
  { to: "/dashboard", label: "Dashboard", icon: "fa-gauge" },
  { to: "/problems", label: "Problems", icon: "fa-triangle-exclamation" },
  { to: "/services", label: "Services", icon: "fa-signal" },
  { to: "/outages", label: "Outages", icon: "fa-plug-circle-xmark" },
  { to: "/announcements", label: "Announcements", icon: "fa-bullhorn" },
  { to: "/apps", label: "Apps", icon: "fa-cube" },
  { to: "/environments", label: "Environments", icon: "fa-layer-group" },
  { to: "/servers", label: "Servers", icon: "fa-server" },
  { to: "/owners", label: "Owners", icon: "fa-building" },
];

function UserMenu({
  fullName,
  email,
  ipAddress,
}: {
  fullName: string;
  email: string;
  ipAddress?: string | null;
}) {
  const { theme, toggle } = useTheme();
  const nav = useNavigate();
  const qc = useQueryClient();
  const displayName = fullName || email;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="h-8 gap-1.5 px-2">
          <i className="fa-sharp fa-regular fa-circle-user" aria-hidden="true" />
          <span className="hidden max-w-40 truncate sm:inline">{displayName}</span>
          <i
            className="fa-sharp fa-regular fa-chevron-down text-xs opacity-60"
            aria-hidden="true"
          />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuLabel className="font-normal">
          <div className="truncate font-medium">{displayName}</div>
          <div className="text-muted-foreground truncate text-xs font-normal">
            {email.toLowerCase()}
          </div>
          {ipAddress && (
            <div className="text-muted-foreground truncate font-mono text-xs font-normal">
              {ipAddress}
            </div>
          )}
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link to="/account">
            <i className="fa-sharp fa-regular fa-circle-user fa-fw" aria-hidden="true" />
            <span className="flex-1">Account</span>
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem asChild>
          <Link to="/security">
            <i className="fa-sharp fa-regular fa-shield-check fa-fw" aria-hidden="true" />
            <span className="flex-1">Security</span>
          </Link>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={toggle}>
          <i
            className={`fa-sharp fa-regular ${theme === "dark" ? "fa-sun" : "fa-moon"} fa-fw`}
            aria-hidden="true"
          />
          <span className="flex-1">
            {theme === "dark" ? "Switch to light mode" : "Switch to dark mode"}
          </span>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onSelect={async () => {
            await api("/api/auth/logout", { method: "POST" });
            await qc.invalidateQueries({ queryKey: ["auth", "me"] });
            nav("/login");
          }}
        >
          <i className="fa-sharp fa-regular fa-arrow-right-from-bracket fa-fw" aria-hidden="true" />
          <span className="flex-1">Sign out</span>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function HelpMenu() {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="h-8 gap-1.5 px-2">
          <i className="fa-sharp fa-regular fa-circle-question" aria-hidden="true" />
          <span className="hidden sm:inline">Help</span>
          <i
            className="fa-sharp fa-regular fa-chevron-down text-xs opacity-60"
            aria-hidden="true"
          />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuItem asChild>
          <a href={`${apiBase}/swagger`} rel="noreferrer">
            <i className="fa-sharp fa-regular fa-book-open fa-fw" aria-hidden="true" />
            <span className="flex-1">API documentation</span>
          </a>
        </DropdownMenuItem>
        <DropdownMenuItem asChild>
          <Link to="/about">
            <i className="fa-sharp fa-regular fa-circle-info fa-fw" aria-hidden="true" />
            <span className="flex-1">About</span>
          </Link>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export function AppShell() {
  const { user, loading } = useAuth();

  if (loading) return <div className="text-muted-foreground p-8">Loading...</div>;
  if (!user) return <Navigate to="/login" replace />;

  return (
    <Shell>
      <SkipLink />

      <Sidebar>
        <SidebarBrand>
          <Link to="/dashboard">Bump</Link>
        </SidebarBrand>
        <SidebarNav>
          {navItems.map((n) => (
            <SidebarNavItem key={n.to} asChild icon={`fa-sharp fa-regular ${n.icon}`}>
              <NavLink to={n.to}>{n.label}</NavLink>
            </SidebarNavItem>
          ))}
        </SidebarNav>
      </Sidebar>

      <ShellBody>
        <Topbar>
          <div className="ml-auto flex items-center gap-1">
            <UserMenu fullName={user.fullName} email={user.email} ipAddress={user.ipAddress} />
            <HelpMenu />
          </div>
        </Topbar>
        <ShellMain>
          <Outlet />
        </ShellMain>
      </ShellBody>
    </Shell>
  );
}
