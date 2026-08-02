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
  { to: "/admin/dashboard", label: "Dashboard" },
  { to: "/admin/problems", label: "Problems" },
  { to: "/admin/services", label: "Services" },
  { to: "/admin/outages", label: "Outages" },
  { to: "/admin/announcements", label: "Announcements" },
  { to: "/admin/apps", label: "Apps" },
  { to: "/admin/environments", label: "Environments" },
  { to: "/admin/tenants", label: "Tenants" },
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
          <Link to="/admin/account">
            <i className="fa-sharp fa-regular fa-circle-user fa-fw" aria-hidden="true" />
            <span className="flex-1">Account</span>
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem asChild>
          <Link to="/admin/security">
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
          <Link to="/admin/about">
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
    <div className="flex h-screen">
      <a
        href="#main"
        className="focus:bg-primary focus:text-primary-foreground sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:rounded focus:px-3 focus:py-2 focus:text-sm"
      >
        Skip to content
      </a>

      <aside aria-label="Sidebar" className="bg-card border-border flex w-56 flex-col border-r">
        <div className="border-border flex h-12 shrink-0 items-center border-b px-4">
          <Link to="/admin/dashboard" className="text-xl font-bold tracking-tight">
            Bump
          </Link>
        </div>
        <nav aria-label="Primary" className="flex-1 space-y-1 overflow-y-auto p-2">
          {navItems.map((n) => (
            <NavLink
              key={n.to}
              to={n.to}
              className={({ isActive }) =>
                `block rounded-lg px-3 py-2 text-sm ${isActive ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted"}`
              }
            >
              {n.label}
            </NavLink>
          ))}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="border-border bg-card flex h-12 shrink-0 items-center gap-2 border-b px-3">
          <div className="ml-auto flex items-center gap-1">
            <UserMenu fullName={user.fullName} email={user.email} ipAddress={user.ipAddress} />
            <HelpMenu />
          </div>
        </header>
        <main id="main" className="relative flex-1 overflow-auto">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
