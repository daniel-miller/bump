import { useEffect, useMemo, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api";
import { useAuth } from "@/hooks/useAuth";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";

function getTimezones(): string[] {
  const intl = Intl as unknown as { supportedValuesOf?: (key: string) => string[] };
  if (typeof intl.supportedValuesOf === "function") {
    try { return intl.supportedValuesOf("timeZone"); } catch { /* fall through */ }
  }
  return ["UTC", "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles", "Europe/London", "Europe/Berlin", "Asia/Tokyo", "Australia/Sydney"];
}

const TIMEZONES = getTimezones();
const TIMEZONE_SET = new Set(TIMEZONES);

export function AccountDetailsPage() {
  const { user, refetch } = useAuth();
  const [fullName, setFullName] = useState("");
  const [timezone, setTimezone] = useState("UTC");
  const [msg, setMsg] = useState<string | null>(null);

  const [newEmail, setNewEmail] = useState("");
  const [emailPassword, setEmailPassword] = useState("");
  const [emailMsg, setEmailMsg] = useState<string | null>(null);

  useEffect(() => {
    if (user) {
      setFullName(user.fullName);
      setTimezone(user.timezone);
    }
  }, [user]);

  const tzValid = useMemo(() => TIMEZONE_SET.has(timezone), [timezone]);

  const save = useMutation({
    mutationFn: () => api("/api/accounts/me", { method: "PATCH", body: JSON.stringify({ fullName, timezone }) }),
    onSuccess: async () => { setMsg("Saved."); await refetch(); },
    onError: (err) => {
      if (err instanceof ApiError) {
        const p = err.problem as { detail?: string; title?: string } | undefined;
        setMsg(p?.detail ?? p?.title ?? err.message);
      } else {
        setMsg("Failed to save.");
      }
    },
  });

  const sendTestEmail = useMutation({
    mutationFn: () => api("/api/accounts/me/test-email", { method: "POST" }),
    onSuccess: () => setMsg(`Test email sent to ${user?.email}.`),
    onError: (err) => {
      if (err instanceof ApiError) {
        const p = err.problem as { detail?: string; title?: string } | undefined;
        setMsg(p?.detail ?? p?.title ?? err.message);
      } else {
        setMsg("Failed to send test email.");
      }
    },
  });

  const requestEmailChange = useMutation({
    mutationFn: () => api("/api/accounts/me/email-changes", {
      method: "POST",
      body: JSON.stringify({ password: emailPassword, newEmail }),
    }),
    onSuccess: () => {
      setEmailMsg(`Check ${newEmail} for a confirmation link.`);
      setNewEmail("");
      setEmailPassword("");
    },
    onError: (err) => {
      if (err instanceof ApiError) {
        const p = err.problem as { detail?: string; title?: string } | undefined;
        setEmailMsg(p?.detail ?? p?.title ?? err.message);
      } else {
        setEmailMsg("Failed to start email change.");
      }
    },
  });

  function onSave() {
    if (!tzValid) {
      setMsg(`'${timezone}' is not a valid IANA timezone (e.g. 'America/Denver').`);
      return;
    }
    setMsg(null);
    save.mutate();
  }

  return (
    <div className="p-6 max-w-xl space-y-4">
      <h1 className="text-2xl font-semibold">Account details</h1>
      <Card>
        <CardContent className="space-y-3 p-4">
          <div className="space-y-1.5">
            <Label htmlFor="fullName">Full name</Label>
            <Input id="fullName" value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label>Email</Label>
            <div className="text-sm text-muted-foreground">{user?.email}</div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="timezone">Timezone</Label>
            <Input
              id="timezone"
              list="timezone-options"
              value={timezone}
              onChange={(e) => setTimezone(e.target.value)}
              aria-invalid={!tzValid}
              className={!tzValid ? "border-danger focus-visible:ring-danger" : undefined}
            />
            <datalist id="timezone-options">
              {TIMEZONES.map((tz) => <option key={tz} value={tz} />)}
            </datalist>
            {!tzValid && (
              <div className="text-xs text-danger">Not a recognized IANA timezone.</div>
            )}
          </div>
          {msg && <div className="text-sm text-muted-foreground">{msg}</div>}
          <div className="flex gap-2">
            <Button onClick={onSave} disabled={!tzValid || save.isPending}>Save changes</Button>
            <Button
              variant="outline"
              onClick={() => { setMsg(null); sendTestEmail.mutate(); }}
              disabled={sendTestEmail.isPending}
            >
              Send test email
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="space-y-3 p-4">
          <div className="text-sm font-semibold">Change email</div>
          <div className="text-xs text-muted-foreground">
            We'll send a confirmation link to the new address. The change is not applied until you click it.
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="newEmail">New email</Label>
            <Input id="newEmail" type="email" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="emailPassword">Current password</Label>
            <Input
              id="emailPassword"
              type="password"
              value={emailPassword}
              onChange={(e) => setEmailPassword(e.target.value)}
              autoComplete="current-password"
            />
          </div>
          {emailMsg && <div className="text-sm text-muted-foreground">{emailMsg}</div>}
          <Button
            onClick={() => requestEmailChange.mutate()}
            disabled={!newEmail || !emailPassword || requestEmailChange.isPending}
          >
            Send confirmation email
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
