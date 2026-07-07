import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api";
import type { MeResponse } from "@/lib/types";

export function useAuth() {
  const q = useQuery<MeResponse | null>({
    queryKey: ["auth", "me"],
    queryFn: async () => {
      try {
        return await api<MeResponse>("/api/accounts/me");
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) return null;
        throw err;
      }
    },
    staleTime: Infinity,
  });
  return { user: q.data, loading: q.isLoading, refetch: q.refetch };
}
