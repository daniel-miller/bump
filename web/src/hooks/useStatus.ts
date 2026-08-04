import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { StatusResponse } from "@/lib/types";

export const STATUS_REFETCH_INTERVAL_MS = 60_000;

export function useStatus(ownerHandle?: string, opts?: { excludePaused?: boolean }) {
  const params = new URLSearchParams();
  if (opts?.excludePaused) params.set("excludePaused", "true");
  const qs = params.toString();
  const path = ownerHandle ? `/api/status/owners/${encodeURIComponent(ownerHandle)}` : "/api/status";
  return useQuery<StatusResponse>({
    queryKey: ["status", ownerHandle ?? "_all", opts?.excludePaused ? "noPaused" : "all"],
    queryFn: () => api<StatusResponse>(`${path}${qs ? `?${qs}` : ""}`),
    refetchInterval: STATUS_REFETCH_INTERVAL_MS,
  });
}
