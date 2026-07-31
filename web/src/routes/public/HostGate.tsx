import { useEffect } from "react";
import { Navigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api";
import { applyTenantTheme, type TenantTheme } from "@/lib/theme";
import { BoardPage } from "@/routes/public/BoardPage";

interface SiteTenant {
  boardSlug: string;
  boardName: string;
  theme?: TenantTheme | null;
}

/**
 * Root-path gate. When the request host matches a tenant's custom hostname
 * (e.g. status.openscorm.com), serve that tenant's board at "/"; otherwise
 * keep the historical redirect to the admin dashboard. A tenant theme, when
 * present, restyles the page (colors, radius, font, favicon) before render.
 */
export function HostGate() {
  const { data, isLoading } = useQuery<SiteTenant | null>({
    queryKey: ["siteTenant"],
    queryFn: async () => {
      try {
        return await api<SiteTenant>("/api/status/site");
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) return null;
        throw e;
      }
    },
    staleTime: Infinity,
    retry: false,
  });

  useEffect(() => {
    if (!data) return;
    document.title = data.boardName;
    if (data.theme) applyTenantTheme(data.theme);
  }, [data]);

  if (isLoading) return <div className="text-muted-foreground p-8">Loading…</div>;
  if (!data) return <Navigate to="/admin/dashboard" replace />;
  return <BoardPage slug={data.boardSlug} logoUrl={data.theme?.logo} />;
}
