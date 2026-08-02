import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, ApiError } from "@/lib/api";

type State =
  | { kind: "working" }
  | { kind: "done" }
  | { kind: "needs-auth" }
  | { kind: "error"; message: string };

export function ConfirmEmailChangePage() {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [state, setState] = useState<State>({ kind: "working" });

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!token) {
        setState({ kind: "error", message: "The confirmation link is missing its token." });
        return;
      }
      try {
        await api("/api/accounts/me/email-changes/confirm", {
          method: "POST",
          body: JSON.stringify({ token }),
        });
        if (!cancelled) setState({ kind: "done" });
      } catch (err) {
        if (cancelled) return;
        if (err instanceof ApiError) {
          if (err.status === 401) {
            setState({ kind: "needs-auth" });
            return;
          }
          const p = err.problem as { detail?: string; title?: string } | undefined;
          setState({ kind: "error", message: p?.detail ?? p?.title ?? err.message });
          return;
        }
        setState({ kind: "error", message: "Couldn't confirm the email change." });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [token]);

  return (
    <div className="mx-auto max-w-md space-y-2 p-8 text-center">
      {state.kind === "working" && <p className="text-muted-foreground">Confirming...</p>}
      {state.kind === "done" && (
        <>
          <h1 className="text-2xl font-semibold">Email updated</h1>
          <p className="text-muted-foreground">Your account email has been changed.</p>
          <p>
            <Link to="/admin/account" className="underline">
              Back to account
            </Link>
          </p>
        </>
      )}
      {state.kind === "needs-auth" && (
        <>
          <h1 className="text-2xl font-semibold">Sign in to confirm</h1>
          <p className="text-muted-foreground">
            Open this link in the same browser you used to request the email change, then sign in
            and click the link again.
          </p>
          <p>
            <Link to="/login" className="underline">
              Sign in
            </Link>
          </p>
        </>
      )}
      {state.kind === "error" && <p className="text-danger">{state.message}</p>}
    </div>
  );
}
