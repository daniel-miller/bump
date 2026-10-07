import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api";
import type { StatusResponse } from "@/lib/types";

export const STATUS_REFETCH_INTERVAL_MS = 60_000;

export function useStatus(ownerHandle?: string, opts?: { excludePaused?: boolean }) {
  const params = new URLSearchParams();
  if (opts?.excludePaused) params.set("excludePaused", "true");
  const qs = params.toString();
  const path = ownerHandle
    ? `/api/status/owners/${encodeURIComponent(ownerHandle)}`
    : "/api/status";
  return useQuery<StatusResponse>({
    queryKey: ["status", ownerHandle ?? "_all", opts?.excludePaused ? "noPaused" : "all"],
    queryFn: () => api<StatusResponse>(`${path}${qs ? `?${qs}` : ""}`),
    refetchInterval: STATUS_REFETCH_INTERVAL_MS,
    // An unknown owner handle will not appear on retry; fail straight to
    // the not-found page instead of holding the loading state.
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 1,
  });
}
