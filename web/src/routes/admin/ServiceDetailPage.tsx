import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  ReferenceLine,
} from "recharts";
import { formatDistanceToNowStrict } from "date-fns";
import { api } from "@/lib/api";
import { formatAbsolute } from "@/lib/dates";
import { StatusDot } from "@/components/StatusDot";
import { HistoryStrip } from "@/components/HistoryStrip";
import type { ServiceStatus } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DangerZone, DangerZoneItem } from "@/components/ui/danger-zone";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

// Mechanical rendering of the infra naming convention: same tokens, one
// separator rule per medium, DNS inverted to most-specific-first.
function canonicalNames(owner: string, environment: string, app?: string | null) {
  const parts = app ? [owner, environment, app] : [owner, environment];
  return {
    site: parts.join("."),
    sql: parts.join("_"),
    label: parts.join("-"),
    host: [...parts].reverse().join(".") + ".<zone>",
  };
}

interface OwnerOption {
  ownerHandle: string;
  ownerName: string;
}

interface EnvironmentOption {
  environmentHandle: string;
  environmentName: string;
}

interface ServiceDetail {
  handle: string;
  name: string;
  url: string;
  owner: string;
  environment: string;
  app: string | null;
  siteId: number | null;
  paused: boolean;
  lastStatus: ServiceStatus;
  latencyMs: number;
  uptime: number;
  history: ServiceStatus[];
  lastCheckAt: string | null;
  lastOutageAt: string | null;
}

interface UptimeResponse {
  range: string;
  points: { day: string; probes: number; uptime: number }[];
}
interface LatencyResponse {
  range: string;
  points: { day: string; probes: number; latencyMs: number }[];
}

const STATUS_LABELS: Record<ServiceStatus, string> = {
  operational: "Up",
  degraded: "Degraded",
  down: "Down",
  unknown: "Unknown",
} as Record<ServiceStatus, string>;

const STATUS_COLORS: Record<ServiceStatus, string> = {
  operational: "text-success",
  degraded: "text-warning",
  down: "text-danger",
  unknown: "text-muted-foreground",
} as Record<ServiceStatus, string>;

export function ServiceDetailPage() {
  const { handle = "" } = useParams<{ handle: string }>();
  const qc = useQueryClient();
  const nav = useNavigate();
  const { data: m } = useQuery<ServiceDetail>({
    queryKey: ["services", handle],
    queryFn: () => api<ServiceDetail>(`/api/admin/services/${handle}`),
    refetchInterval: 10_000,
  });

  // Tick once per second so "X seconds ago" updates without a query refetch.
  const [, setTick] = useState(0);
  useEffect(() => {
    const id = window.setInterval(() => setTick((t) => t + 1), 1000);
    return () => window.clearInterval(id);
  }, []);
  const { data: ts } = useQuery<LatencyResponse>({
    queryKey: ["services", handle, "latency", "7d"],
    queryFn: () => api<LatencyResponse>(`/api/admin/services/${handle}/latency?range=7d`),
    enabled: !!handle,
  });
  const { data: ts24 } = useQuery<UptimeResponse>({
    queryKey: ["services", handle, "uptime", "24h"],
    queryFn: () => api<UptimeResponse>(`/api/admin/services/${handle}/uptime?range=24h`),
    enabled: !!handle,
  });

  const remove = useMutation({
    mutationFn: () => api(`/api/admin/services/${handle}`, { method: "DELETE" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["services"] });
      nav("/services");
    },
  });

  const pause = useMutation({
    mutationFn: () => api(`/api/admin/services/${handle}/pause`, { method: "POST" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["services", handle] });
      await qc.invalidateQueries({ queryKey: ["services"] });
    },
  });

  const resume = useMutation({
    mutationFn: () => api(`/api/admin/services/${handle}/resume`, { method: "POST" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["services", handle] });
      await qc.invalidateQueries({ queryKey: ["services"] });
    },
  });

  const [editing, setEditing] = useState(false);
  const [edit, setEdit] = useState({ name: "", url: "", owner: "", environment: "", siteId: "" });
  const [editError, setEditError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);

  // Canon tokens only: owner and environment come from the rosters, so an
  // alias or a typo can never be written. Aliases are for reading.
  const { data: owners = [] } = useQuery<OwnerOption[]>({
    queryKey: ["owners"],
    queryFn: () => api<OwnerOption[]>("/api/admin/owners"),
    enabled: editing,
  });
  const { data: environments = [] } = useQuery<EnvironmentOption[]>({
    queryKey: ["admin", "environments"],
    queryFn: () => api<EnvironmentOption[]>("/api/admin/environments"),
    enabled: editing,
  });

  const save = useMutation({
    mutationFn: () =>
      api(`/api/admin/services/${handle}`, {
        method: "PATCH",
        body: JSON.stringify({
          name: edit.name,
          url: edit.url,
          owner: edit.owner,
          environment: edit.environment,
          // 0 clears a previously recorded site ID.
          siteId: edit.siteId.trim() === "" ? 0 : Number(edit.siteId),
        }),
      }),
    onSuccess: async () => {
      setEditing(false);
      setEditError(null);
      await qc.invalidateQueries({ queryKey: ["services", handle] });
      await qc.invalidateQueries({ queryKey: ["services"] });
    },
    onError: (err: Error) => setEditError(err.message),
  });

  function startEdit() {
    if (!m) return;
    setEdit({
      name: m.name,
      url: m.url,
      owner: m.owner,
      environment: m.environment,
      siteId: m.siteId?.toString() ?? "",
    });
    setEditError(null);
    setEditing(true);
  }

  if (!m) return <div className="text-muted-foreground p-8">Loading...</div>;

  const isUp = m.lastStatus === "operational";
  const statusLabel = m.paused ? "Paused" : (STATUS_LABELS[m.lastStatus] ?? m.lastStatus);
  const statusColor = m.paused
    ? "text-muted-foreground"
    : (STATUS_COLORS[m.lastStatus] ?? "text-foreground");

  let statusHelper: string;
  if (m.paused) {
    statusHelper = "Monitoring paused — no checks are running";
  } else if (isUp) {
    statusHelper = m.lastOutageAt
      ? `Up for ${formatDistanceToNowStrict(new Date(m.lastOutageAt))}`
      : "Operational";
  } else if (m.lastOutageAt) {
    statusHelper = `Down since ${formatAbsolute(m.lastOutageAt)}`;
  } else {
    statusHelper = "Last check failed";
  }

  const lastCheckLabel = m.lastCheckAt
    ? `${formatDistanceToNowStrict(new Date(m.lastCheckAt))} ago`
    : "Never";

  const last24 = ts24?.points?.[0];
  const last24Uptime = last24 ? Number(last24.uptime).toFixed(2) : null;
  const last24Probes = last24?.probes ?? 0;
  const last24Failures = last24
    ? Math.max(0, last24.probes - Math.round((last24.uptime / 100) * last24.probes))
    : 0;

  return (
    <div className="space-y-6 p-6">
      <header className="flex items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-semibold">
            <StatusDot status={m.lastStatus} size={14} />
            {m.name}
          </h1>
          <div className="text-sm">
            <a
              href={m.url}
              target="_blank"
              rel="noopener noreferrer"
              className="text-primary break-all hover:underline"
            >
              {m.url}
            </a>
          </div>
          <div className="text-muted-foreground mt-0.5 text-xs">
            {m.owner} / {m.environment}
            {m.app && <span className="ml-1">· app {m.app}</span>}
          </div>
        </div>
        <div className="flex flex-col items-end gap-2">
          <div className="text-muted-foreground text-sm">
            {m.latencyMs} ms · {Number(m.uptime).toFixed(2)}%
          </div>
          <div className="flex items-center gap-3">
            <Button variant="outline" onClick={startEdit} disabled={editing}>
              Edit
            </Button>
            {m.paused ? (
              <Button variant="outline" onClick={() => resume.mutate()} disabled={resume.isPending}>
                Resume
              </Button>
            ) : (
              <Button variant="outline" onClick={() => pause.mutate()} disabled={pause.isPending}>
                Pause
              </Button>
            )}
          </div>
        </div>
      </header>

      {editing && (
        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Edit service</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="edit-name">Name</Label>
                <Input
                  id="edit-name"
                  value={edit.name}
                  onChange={(e) => setEdit({ ...edit, name: e.target.value })}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="edit-url">URL</Label>
                <Input
                  id="edit-url"
                  value={edit.url}
                  onChange={(e) => setEdit({ ...edit, url: e.target.value })}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="edit-owner">Owner</Label>
                <Select
                  value={edit.owner}
                  onValueChange={(v) => setEdit({ ...edit, owner: v })}
                >
                  <SelectTrigger id="edit-owner">
                    <SelectValue placeholder="Select owner" />
                  </SelectTrigger>
                  <SelectContent>
                    {owners.map((o) => (
                      <SelectItem key={o.ownerHandle} value={o.ownerHandle}>
                        <span className="font-mono">{o.ownerHandle}</span>
                        <span className="text-muted-foreground ml-2 text-xs">{o.ownerName}</span>
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="edit-environment">Environment</Label>
                <Select
                  value={edit.environment}
                  onValueChange={(v) => setEdit({ ...edit, environment: v })}
                >
                  <SelectTrigger id="edit-environment">
                    <SelectValue placeholder="Select environment" />
                  </SelectTrigger>
                  <SelectContent>
                    {environments.map((e) => (
                      <SelectItem key={e.environmentHandle} value={e.environmentHandle}>
                        <span className="font-mono">{e.environmentHandle}</span>
                        <span className="text-muted-foreground ml-2 text-xs">
                          {e.environmentName}
                        </span>
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="edit-site-id">IIS site ID</Label>
                <Input
                  id="edit-site-id"
                  inputMode="numeric"
                  placeholder="e.g. 10310"
                  className="font-mono"
                  value={edit.siteId}
                  onChange={(e) =>
                    setEdit({ ...edit, siteId: e.target.value.replace(/[^0-9]/g, "") })
                  }
                />
                <p className="text-muted-foreground text-xs">
                  STTEA digits assigned in IIS; leave empty when not IIS-hosted
                </p>
              </div>
            </div>
            <div className="text-muted-foreground text-xs">
              Handle <code className="font-mono">{m.handle}</code> cannot be changed.
            </div>
            {editError && <div className="text-danger text-sm">{editError}</div>}
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setEditing(false)}>
                Cancel
              </Button>
              <Button onClick={() => save.mutate()} disabled={save.isPending}>
                Save changes
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      <section className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <Card>
          <CardContent className="p-4">
            <div className="text-muted-foreground text-xs">Current status</div>
            <div className={`mt-1 text-2xl font-semibold ${statusColor}`}>{statusLabel}</div>
            <div className="text-muted-foreground mt-1 text-xs">{statusHelper}</div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="p-4">
            <div className="text-muted-foreground text-xs">Last check</div>
            <div className="mt-1 text-2xl font-semibold">{lastCheckLabel}</div>
            <div className="text-muted-foreground mt-1 text-xs">
              {m.paused ? "Paused" : "Auto-refreshing"}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="p-4">
            <div className="flex items-center justify-between">
              <div className="text-muted-foreground text-xs">Last 24 hours</div>
              <div className="text-sm font-semibold">
                {last24Uptime !== null ? `${last24Uptime}%` : "—"}
              </div>
            </div>
            <div className="mt-2">
              <HistoryStrip history={m.history} height={20} />
            </div>
            <div className="text-muted-foreground mt-2 text-xs">
              {last24Probes} {last24Probes === 1 ? "check" : "checks"}
              {last24Failures > 0 && ` · ${last24Failures} failed`}
            </div>
          </CardContent>
        </Card>
      </section>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Recent</CardTitle>
        </CardHeader>
        <CardContent>
          <HistoryStrip history={m.history} height={40} />
        </CardContent>
      </Card>

      {ts && (
        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Response time (7d average ms)</CardTitle>
          </CardHeader>
          <CardContent>
            <div style={{ width: "100%", height: 240 }}>
              <ResponsiveContainer>
                <LineChart data={ts.points}>
                  <XAxis dataKey="day" stroke="#888" />
                  <YAxis stroke="#888" />
                  <Tooltip
                    contentStyle={{
                      background: "var(--color-card)",
                      border: "1px solid var(--color-border)",
                    }}
                  />
                  <ReferenceLine y={1000} stroke="#facc15" strokeDasharray="3 3" />
                  <Line
                    type="monotone"
                    dataKey="latencyMs"
                    stroke="var(--color-primary)"
                    strokeWidth={2}
                    dot={false}
                  />
                </LineChart>
              </ResponsiveContainer>
            </div>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Canonical names</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {(() => {
            const names = canonicalNames(m.owner, m.environment, m.app);
            const rows: [string, string][] = [
              ["IIS site", names.site],
              ["SQL identifier", names.sql],
              ["Task / label media", names.label],
              ["DNS hostname", names.host],
            ];
            return rows.map(([label, value]) => (
              <div key={label} className="flex items-baseline gap-3 text-sm">
                <span className="text-muted-foreground w-36 shrink-0 text-xs">{label}</span>
                <span className="font-mono">{value}</span>
              </div>
            ));
          })()}
          {m.siteId !== null && (
            <div className="flex items-baseline gap-3 text-sm">
              <span className="text-muted-foreground w-36 shrink-0 text-xs">IIS site ID</span>
              <span className="font-mono">
                {m.siteId}
                <span className="text-muted-foreground ml-2">logs in W3SVC{m.siteId}</span>
              </span>
            </div>
          )}
          <p className="text-muted-foreground pt-1 text-xs">
            Derived mechanically from the owner, environment, and app tokens per the infra naming
            convention; deployed names may differ
          </p>
        </CardContent>
      </Card>

      <DangerZone>
        <DangerZoneItem
          title="Delete service"
          description="Permanently delete this service and all probe history. This cannot be undone."
          action={
            <Button
              variant="destructive"
              onClick={() => setConfirmOpen(true)}
              disabled={remove.isPending}
            >
              Delete service
            </Button>
          }
        />
      </DangerZone>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title="Delete service?"
        description={`"${m.name}" will be removed permanently along with all probe history. This cannot be undone.`}
        confirmLabel="Delete service"
        variant="danger"
        disabled={remove.isPending}
        onConfirm={() => {
          setConfirmOpen(false);
          remove.mutate();
        }}
      />
    </div>
  );
}
