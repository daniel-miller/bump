import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatAbsolute } from "@/lib/dates";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DangerZone, DangerZoneItem } from "@/components/ui/danger-zone";

const STATUS_LABELS: Record<string, string> = {
  investigating: "Investigating",
  identified: "Identified",
  monitoring: "Monitoring",
  resolved: "Resolved",
};

interface OutageDetail {
  outage: {
    outageId: number;
    outageTitle: string;
    outageStatus: string;
    startedAt: string;
    resolvedAt: string | null;
    rootCause: string | null;
  };
  updates: { updateId: number; statusAtUpdate: string; updateMessage: string; createdAt: string }[];
  service: { id: number; slug: string; name: string; url: string } | null;
}

export function OutageDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const qc = useQueryClient();
  const nav = useNavigate();
  const { data } = useQuery<OutageDetail>({
    queryKey: ["outages", id],
    queryFn: () => api<OutageDetail>(`/api/admin/outages/${id}`),
  });
  const [status, setStatus] = useState("investigating");
  const [message, setMessage] = useState("");
  const [confirmOpen, setConfirmOpen] = useState(false);
  const append = useMutation({
    mutationFn: () =>
      api(`/api/admin/outages/${id}/updates`, {
        method: "POST",
        body: JSON.stringify({ status, message, published: true }),
      }),
    onSuccess: async () => {
      setMessage("");
      await qc.invalidateQueries({ queryKey: ["outages", id] });
    },
  });
  const remove = useMutation({
    mutationFn: () => api(`/api/admin/outages/${id}`, { method: "DELETE" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["outages"] });
      await qc.invalidateQueries({ queryKey: ["outages", "all"] });
      nav("/admin/outages");
    },
  });

  if (!data) return <div className="text-muted-foreground p-8">Loading...</div>;
  return (
    <div className="max-w-3xl space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold">{data.outage.outageTitle}</h1>
        <div className="text-muted-foreground text-sm">
          {STATUS_LABELS[data.outage.outageStatus] ?? data.outage.outageStatus} · started{" "}
          {formatAbsolute(data.outage.startedAt)}
          {data.outage.resolvedAt && ` · resolved ${formatAbsolute(data.outage.resolvedAt)}`}
        </div>
        {data.service && (
          <div className="mt-1 text-sm">
            <span className="text-muted-foreground">Service: </span>
            <a
              href={data.service.url}
              target="_blank"
              rel="noopener noreferrer"
              className="text-primary break-all hover:underline"
              title="Open the service in a new tab to check if it is still down"
            >
              {data.service.name}
            </a>
          </div>
        )}
      </div>
      <section className="space-y-2">
        {data.updates.map((u) => (
          <Card key={u.updateId}>
            <CardContent className="p-3">
              <div className="text-muted-foreground text-xs">
                {formatAbsolute(u.createdAt)} ·{" "}
                {STATUS_LABELS[u.statusAtUpdate] ?? u.statusAtUpdate}
              </div>
              <div className="mt-1 whitespace-pre-wrap">{u.updateMessage}</div>
            </CardContent>
          </Card>
        ))}
      </section>
      <Card>
        <CardContent className="space-y-3 p-4">
          <div className="w-56 space-y-1.5">
            <Label htmlFor="outage-status">Status</Label>
            <Select value={status} onValueChange={setStatus}>
              <SelectTrigger id="outage-status">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="investigating">Investigating</SelectItem>
                <SelectItem value="identified">Identified</SelectItem>
                <SelectItem value="monitoring">Monitoring</SelectItem>
                <SelectItem value="resolved">Resolved</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="outage-update">Update message</Label>
            <Textarea
              id="outage-update"
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              rows={3}
              placeholder="What changed? Subscribers see this immediately."
            />
          </div>
          <div className="flex justify-end">
            <Button onClick={() => append.mutate()} disabled={!message.trim() || append.isPending}>
              Post update
            </Button>
          </div>
        </CardContent>
      </Card>

      <DangerZone>
        <DangerZoneItem
          title="Delete outage"
          description="Permanently delete this outage and all its updates. Subscribers won't be notified."
          action={
            <Button
              variant="destructive"
              onClick={() => setConfirmOpen(true)}
              disabled={remove.isPending}
            >
              Delete outage
            </Button>
          }
        />
      </DangerZone>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title="Delete outage?"
        description={`"${data.outage.outageTitle}" and all its updates will be removed permanently. This cannot be undone.`}
        confirmLabel="Delete outage"
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
