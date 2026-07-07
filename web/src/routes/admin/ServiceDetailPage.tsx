import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, ReferenceLine } from "recharts";
import { formatDistanceToNowStrict } from "date-fns";
import { api } from "@/lib/api";
import { StatusDot } from "@/components/StatusDot";
import { HistoryStrip } from "@/components/HistoryStrip";
import type { ServiceStatus } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DangerZone, DangerZoneItem } from "@/components/ui/danger-zone";

function slugifyTag(input: string): string {
  return input.toLowerCase().replace(/[^a-z0-9_-]/g, "");
}

interface ServiceDetail {
  slug: string;
  name: string;
  url: string;
  tenant: string;
  environment: string;
  paused: boolean;
  isPrivate: boolean;
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
  const { slug = "" } = useParams<{ slug: string }>();
  const qc = useQueryClient();
  const nav = useNavigate();
  const { data: m } = useQuery<ServiceDetail>({
    queryKey: ["services", slug],
    queryFn: () => api<ServiceDetail>(`/api/admin/services/${slug}`),
    refetchInterval: 10_000,
  });

  // Tick once per second so "X seconds ago" updates without a query refetch.
  const [, setTick] = useState(0);
  useEffect(() => {
    const id = window.setInterval(() => setTick((t) => t + 1), 1000);
    return () => window.clearInterval(id);
  }, []);
  const { data: ts } = useQuery<LatencyResponse>({
    queryKey: ["services", slug, "latency", "7d"],
    queryFn: () => api<LatencyResponse>(`/api/admin/services/${slug}/latency?range=7d`),
    enabled: !!slug,
  });
  const { data: ts24 } = useQuery<UptimeResponse>({
    queryKey: ["services", slug, "uptime", "24h"],
    queryFn: () => api<UptimeResponse>(`/api/admin/services/${slug}/uptime?range=24h`),
    enabled: !!slug,
  });

  const remove = useMutation({
    mutationFn: () => api(`/api/admin/services/${slug}`, { method: "DELETE" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["services"] });
      nav("/admin/services");
    },
  });

  const pause = useMutation({
    mutationFn: () => api(`/api/admin/services/${slug}/pause`, { method: "POST" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["services", slug] });
      await qc.invalidateQueries({ queryKey: ["services"] });
    },
  });

  const resume = useMutation({
    mutationFn: () => api(`/api/admin/services/${slug}/resume`, { method: "POST" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["services", slug] });
      await qc.invalidateQueries({ queryKey: ["services"] });
    },
  });

  const setPrivate = useMutation({
    mutationFn: (isPrivate: boolean) =>
      api(`/api/admin/services/${slug}`, {
        method: "PATCH",
        body: JSON.stringify({ isPrivate }),
      }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["services", slug] });
      await qc.invalidateQueries({ queryKey: ["services"] });
    },
  });

  const [editing, setEditing] = useState(false);
  const [edit, setEdit] = useState({ name: "", url: "", tenant: "", environment: "" });
  const [editError, setEditError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const save = useMutation({
    mutationFn: () => api(`/api/admin/services/${slug}`, { method: "PATCH", body: JSON.stringify(edit) }),
    onSuccess: async () => {
      setEditing(false);
      setEditError(null);
      await qc.invalidateQueries({ queryKey: ["services", slug] });
      await qc.invalidateQueries({ queryKey: ["services"] });
    },
    onError: (err: Error) => setEditError(err.message),
  });

  function startEdit() {
    if (!m) return;
    setEdit({ name: m.name, url: m.url, tenant: m.tenant, environment: m.environment });
    setEditError(null);
    setEditing(true);
  }

  if (!m) return <div className="p-8 text-muted-foreground">Loading…</div>;

  const isUp = m.lastStatus === "operational";
  const statusLabel = m.paused ? "Paused" : (STATUS_LABELS[m.lastStatus] ?? m.lastStatus);
  const statusColor = m.paused ? "text-muted-foreground" : (STATUS_COLORS[m.lastStatus] ?? "text-foreground");

  let statusHelper: string;
  if (m.paused) {
    statusHelper = "Monitoring paused — no checks are running";
  } else if (isUp) {
    statusHelper = m.lastOutageAt
      ? `Up for ${formatDistanceToNowStrict(new Date(m.lastOutageAt))}`
      : "Operational";
  } else if (m.lastOutageAt) {
    statusHelper = `Down since ${new Date(m.lastOutageAt).toLocaleString()}`;
  } else {
    statusHelper = "Last check failed";
  }

  const lastCheckLabel = m.lastCheckAt
    ? `${formatDistanceToNowStrict(new Date(m.lastCheckAt))} ago`
    : "Never";

  const last24 = ts24?.points?.[0];
  const last24Uptime = last24 ? Number(last24.uptime).toFixed(2) : null;
  const last24Probes = last24?.probes ?? 0;
  const last24Failures = last24 ? Math.max(0, last24.probes - Math.round((last24.uptime / 100) * last24.probes)) : 0;

  return (
    <div className="p-6 space-y-6">
      <header className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold flex items-center gap-2">
            <StatusDot status={m.lastStatus} size={14} />
            {m.name}
          </h1>
          <div className="text-sm">
            <a
              href={m.url}
              target="_blank"
              rel="noopener noreferrer"
              className="text-primary hover:underline break-all"
            >
              {m.url}
            </a>
          </div>
          <div className="text-xs text-muted-foreground mt-0.5">{m.tenant} / {m.environment}</div>
        </div>
        <div className="flex flex-col items-end gap-2">
          <div className="text-sm text-muted-foreground">
            {m.latencyMs} ms · {Number(m.uptime).toFixed(2)}%
          </div>
          <div className="flex items-center gap-3">
            <Label htmlFor="is-private" className="flex items-center gap-2 text-sm font-normal cursor-pointer">
              <Checkbox
                id="is-private"
                checked={m.isPrivate}
                disabled={setPrivate.isPending}
                onCheckedChange={(v) => setPrivate.mutate(v === true)}
              />
              Private
            </Label>
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
                <Input id="edit-name" value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="edit-url">URL</Label>
                <Input id="edit-url" value={edit.url} onChange={(e) => setEdit({ ...edit, url: e.target.value })} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="edit-tenant">Tenant</Label>
                <Input
                  id="edit-tenant"
                  value={edit.tenant}
                  onChange={(e) => setEdit({ ...edit, tenant: slugifyTag(e.target.value) })}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="edit-environment">Environment</Label>
                <Input
                  id="edit-environment"
                  value={edit.environment}
                  onChange={(e) => setEdit({ ...edit, environment: slugifyTag(e.target.value) })}
                />
              </div>
            </div>
            <div className="text-xs text-muted-foreground">
              Slug <code className="font-mono">{m.slug}</code> cannot be changed.
            </div>
            {editError && <div className="text-sm text-danger">{editError}</div>}
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setEditing(false)}>Cancel</Button>
              <Button onClick={() => save.mutate()} disabled={save.isPending}>Save changes</Button>
            </div>
          </CardContent>
        </Card>
      )}

      <section className="grid grid-cols-1 md:grid-cols-3 gap-3">
        <Card>
          <CardContent className="p-4">
            <div className="text-xs text-muted-foreground">Current status</div>
            <div className={`text-2xl font-semibold mt-1 ${statusColor}`}>{statusLabel}</div>
            <div className="text-xs text-muted-foreground mt-1">{statusHelper}</div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="p-4">
            <div className="text-xs text-muted-foreground">Last check</div>
            <div className="text-2xl font-semibold mt-1">{lastCheckLabel}</div>
            <div className="text-xs text-muted-foreground mt-1">
              {m.paused ? "Paused" : "Auto-refreshing"}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent className="p-4">
            <div className="flex items-center justify-between">
              <div className="text-xs text-muted-foreground">Last 24 hours</div>
              <div className="text-sm font-semibold">{last24Uptime !== null ? `${last24Uptime}%` : "—"}</div>
            </div>
            <div className="mt-2">
              <HistoryStrip history={m.history} height={20} />
            </div>
            <div className="text-xs text-muted-foreground mt-2">
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
                  <Tooltip contentStyle={{ background: "var(--color-card)", border: "1px solid var(--color-border)" }} />
                  <ReferenceLine y={1000} stroke="#facc15" strokeDasharray="3 3" />
                  <Line type="monotone" dataKey="latencyMs" stroke="var(--color-primary)" strokeWidth={2} dot={false} />
                </LineChart>
              </ResponsiveContainer>
            </div>
          </CardContent>
        </Card>
      )}

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
