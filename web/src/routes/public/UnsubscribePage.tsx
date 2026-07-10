import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { api } from "@/lib/api";

export function UnsubscribePage() {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [state, setState] = useState<"working" | "done" | "error">("working");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!token) return setState("error");
      try {
        await api(`/api/subscribers/unsubscribe?token=${encodeURIComponent(token)}`, {
          method: "POST",
        });
        if (!cancelled) setState("done");
      } catch {
        if (!cancelled) setState("error");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [token]);

  return (
    <div className="mx-auto max-w-md p-8 text-center">
      {state === "working" && <p className="text-muted-foreground">Unsubscribing…</p>}
      {state === "done" && (
        <p>You're unsubscribed. You won't get further updates from this status page.</p>
      )}
      {state === "error" && (
        <p className="text-danger">
          Couldn't unsubscribe. The link may have expired. Contact support if this keeps happening.
        </p>
      )}
    </div>
  );
}
