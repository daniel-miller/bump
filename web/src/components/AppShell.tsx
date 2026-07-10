import { Button } from "@/components/ui/button";
import { useAuth } from "@/hooks/useAuth";
import { useTheme } from "@/hooks/useTheme";
import { api, apiBase } from "@/lib/api";
import { useQueryClient } from "@tanstack/react-query";
import { BookOpen, Info, Moon, ShieldCheck, Sun, UserCircle } from "lucide-react";
import { Link, NavLink, Navigate, Outlet, useNavigate } from "react-router-dom";

export function AppShell() {
  const { user, loading } = useAuth();
  const { theme, toggle } = useTheme();
  const nav = useNavigate();
  const qc = useQueryClient();

  if (loading) return <div className="text-muted-foreground p-8">Loading…</div>;
  if (!user) return <Navigate to="/login" replace />;

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

  return (
    <div className="flex h-screen">
      <aside className="bg-card border-border flex w-56 flex-col border-r">
        <div className="border-border flex items-center justify-between border-b p-4">
          <Link to="/admin/dashboard" className="flex items-baseline gap-1.5">
            <span className="text-xl font-bold">Bump</span>
          </Link>
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7"
            onClick={toggle}
            aria-label={theme === "dark" ? "Switch to light mode" : "Switch to dark mode"}
            title={theme === "dark" ? "Switch to light mode" : "Switch to dark mode"}
          >
            {theme === "dark" ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
          </Button>
        </div>
        <nav className="flex-1 space-y-1 p-2">
          {navItems.map((n) => (
            <NavLink
              key={n.to}
              to={n.to}
              className={({ isActive }) =>
                `block rounded px-3 py-2 text-sm ${isActive ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted"}`
              }
            >
              {n.label}
            </NavLink>
          ))}
        </nav>
        <div className="border-border space-y-1 border-t p-2">
          <NavLink
            to="/admin/account"
            className={({ isActive }) =>
              `flex items-center gap-2 rounded px-3 py-2 text-sm ${isActive ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted"}`
            }
          >
            <UserCircle className="h-4 w-4" />
            Account
          </NavLink>
          <NavLink
            to="/admin/security"
            className={({ isActive }) =>
              `flex items-center gap-2 rounded px-3 py-2 text-sm ${isActive ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted"}`
            }
          >
            <ShieldCheck className="h-4 w-4" />
            Security
          </NavLink>
          <a
            href={`${apiBase}/swagger`}
            rel="noreferrer"
            className="text-muted-foreground hover:bg-muted flex items-center gap-2 rounded px-3 py-2 text-sm"
          >
            <BookOpen className="h-4 w-4" />
            API Documentation
          </a>
          <NavLink
            to="/admin/about"
            className={({ isActive }) =>
              `flex items-center gap-2 rounded px-3 py-2 text-sm ${isActive ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted"}`
            }
          >
            <Info className="h-4 w-4" />
            About
          </NavLink>
        </div>
        <div className="border-border border-t p-3 text-sm">
          <div className="text-muted-foreground truncate">{user.email}</div>
          {user.ipAddress && (
            <div className="text-muted-foreground/70 truncate font-mono text-xs">
              {user.ipAddress}
            </div>
          )}
          <Button
            variant="ghost"
            size="sm"
            className="text-muted-foreground hover:text-foreground mt-2 h-7 px-2 text-xs"
            onClick={async () => {
              await api("/api/auth/logout", { method: "POST" });
              await qc.invalidateQueries({ queryKey: ["auth", "me"] });
              nav("/login");
            }}
          >
            Sign out
          </Button>
        </div>
      </aside>
      <main className="flex-1 overflow-auto">
        <Outlet />
      </main>
    </div>
  );
}
