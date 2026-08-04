import { useEffect } from "react";
import { Navigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api";
import { applyOwnerTheme, type OwnerTheme } from "@/lib/theme";
import { BoardPage } from "@/routes/public/BoardPage";

interface SiteOwner {
  ownerHandle: string;
  ownerName: string;
  theme?: OwnerTheme | null;
}

/**
 * Root-path gate. When the request host matches an owner's custom hostname
 * (e.g. status.openscorm.com), serve that owner's board at "/"; otherwise
 * keep the historical redirect to the admin dashboard. An owner theme, when
 * present, restyles the page (colors, radius, font, favicon) before render.
 */
export function HostGate() {
  const { data, isLoading } = useQuery<SiteOwner | null>({
    queryKey: ["siteOwner"],
    queryFn: async () => {
      try {
        return await api<SiteOwner>("/api/status/site");
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
    document.title = data.ownerName;
    if (data.theme) applyOwnerTheme(data.theme);
  }, [data]);

  if (isLoading) return <div className="text-muted-foreground p-8">Loading...</div>;
  if (!data) return <Navigate to="/dashboard" replace />;
  return <BoardPage handle={data.ownerHandle} logoUrl={data.theme?.logo} />;
}
