import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { api, ApiError } from "@/lib/api";
import { useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [totp, setTotp] = useState("");
  const [needsTotp, setNeedsTotp] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const nav = useNavigate();
  const qc = useQueryClient();

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await api("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ email, password, totp: totp || undefined }),
      });
      await qc.invalidateQueries({ queryKey: ["auth", "me"] });
      nav("/admin/dashboard");
    } catch (err) {
      if (err instanceof ApiError) {
        const p = err.problem as { title?: string; detail?: string } | undefined;
        const msg = p?.detail ?? p?.title ?? err.message;
        if (msg.toLowerCase().includes("two-factor") || msg.toLowerCase().includes("totp")) {
          setNeedsTotp(true);
        }
        setError(msg);
      } else {
        setError("Couldn't sign in. Check your email and password.");
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-8">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-xl">Sign in</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                autoComplete="email"
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                autoComplete="current-password"
              />
            </div>
            {needsTotp && (
              <div className="space-y-1.5">
                <Label htmlFor="totp">Authenticator code</Label>
                <Input
                  id="totp"
                  type="text"
                  inputMode="numeric"
                  value={totp}
                  onChange={(e) => setTotp(e.target.value)}
                  autoComplete="one-time-code"
                />
              </div>
            )}
            {error && (
              <div className="text-danger text-sm break-words whitespace-pre-wrap">{error}</div>
            )}
            <Button type="submit" disabled={busy} className="w-full">
              {busy ? "Signing in..." : "Sign in"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
