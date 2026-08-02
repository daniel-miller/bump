import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api";
import { useAuth } from "@/hooks/useAuth";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function AccountSecurityPage() {
  const { user, refetch } = useAuth();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [pwMsg, setPwMsg] = useState<string | null>(null);
  const [setupSecret, setSetupSecret] = useState<string | null>(null);
  const [setupUri, setSetupUri] = useState<string | null>(null);
  const [setupCodes, setSetupCodes] = useState<string[] | null>(null);
  const [setupPassword, setSetupPassword] = useState("");
  const [verifyCode, setVerifyCode] = useState("");
  const [twoMsg, setTwoMsg] = useState<string | null>(null);

  const changePw = useMutation({
    mutationFn: () =>
      api("/api/accounts/me/password", {
        method: "POST",
        body: JSON.stringify({ currentPassword: current, newPassword: next }),
      }),
    onSuccess: () => {
      setPwMsg("Password changed. You'll need to sign in on other devices.");
      setCurrent("");
      setNext("");
    },
    onError: (err) =>
      setPwMsg(
        err instanceof ApiError
          ? `Couldn't change password. ${(err.problem as { detail?: string } | undefined)?.detail ?? err.message}`
          : "Couldn't change password. Try again.",
      ),
  });

  const startSetup = useMutation({
    mutationFn: () =>
      api<{ secret: string; otpAuthUri: string; recoveryCodes: string[] }>("/api/accounts/me/mfa", {
        method: "POST",
        body: JSON.stringify({ password: setupPassword }),
      }),
    onSuccess: (r) => {
      setSetupSecret(r.secret);
      setSetupUri(r.otpAuthUri);
      setSetupCodes(r.recoveryCodes);
      setSetupPassword("");
      setTwoMsg(null);
    },
    onError: (err) =>
      setTwoMsg(
        err instanceof ApiError
          ? `Couldn't start setup. ${(err.problem as { detail?: string } | undefined)?.detail ?? err.message}`
          : "Couldn't start setup. Try again.",
      ),
  });

  const verify = useMutation({
    mutationFn: () =>
      api("/api/accounts/me/mfa/verifications", {
        method: "POST",
        body: JSON.stringify({ code: verifyCode }),
      }),
    onSuccess: async () => {
      setTwoMsg("Two-factor authentication is on.");
      setSetupSecret(null);
      setSetupUri(null);
      setSetupCodes(null);
      await refetch();
    },
    onError: () => setTwoMsg("That code didn't match. Try again."),
  });

  return (
    <div className="max-w-2xl space-y-6 p-6">
      <h1 className="text-2xl font-semibold">Security</h1>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Change password</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="space-y-1.5">
            <Label htmlFor="current">Current password</Label>
            <Input
              id="current"
              type="password"
              value={current}
              onChange={(e) => setCurrent(e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="next">New password</Label>
            <Input
              id="next"
              type="password"
              value={next}
              onChange={(e) => setNext(e.target.value)}
            />
          </div>
          {pwMsg && <div className="text-muted-foreground text-sm">{pwMsg}</div>}
          <Button onClick={() => changePw.mutate()}>Save changes</Button>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Two-factor authentication</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="text-muted-foreground text-sm">{user?.totpEnabled ? "On" : "Off"}</div>
          {!user?.totpEnabled && !setupSecret && (
            <div className="space-y-2">
              <div className="space-y-1.5">
                <Label htmlFor="setup-password">Confirm current password</Label>
                <Input
                  id="setup-password"
                  type="password"
                  value={setupPassword}
                  onChange={(e) => setSetupPassword(e.target.value)}
                  autoComplete="current-password"
                />
              </div>
              <Button
                onClick={() => startSetup.mutate()}
                disabled={!setupPassword || startSetup.isPending}
              >
                Set up two-factor authentication
              </Button>
            </div>
          )}
          {setupSecret && (
            <div className="space-y-2">
              <div className="text-sm">
                Scan this with your authenticator app, or enter the secret manually.
              </div>
              <code className="bg-muted block rounded-md p-2 text-xs break-all">{setupSecret}</code>
              <code className="bg-muted block rounded-md p-2 text-xs break-all">{setupUri}</code>
              <div className="text-sm">Save these recovery codes — you won't see them again.</div>
              <ul className="space-y-1 font-mono text-xs">
                {setupCodes?.map((c) => (
                  <li key={c}>{c}</li>
                ))}
              </ul>
              <Input
                value={verifyCode}
                onChange={(e) => setVerifyCode(e.target.value)}
                placeholder="Enter 6-digit code"
              />
              <Button
                onClick={() => verify.mutate()}
                disabled={verifyCode.length < 6 || verify.isPending}
              >
                Verify and enable
              </Button>
            </div>
          )}
          {twoMsg && <div className="text-muted-foreground text-sm">{twoMsg}</div>}
        </CardContent>
      </Card>
    </div>
  );
}
