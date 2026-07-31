import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DangerZone, DangerZoneItem } from "@/components/ui/danger-zone";

interface BoardDetail {
  board: { boardId: number; boardSlug: string; boardName: string; boardHost: string | null };
  serviceIds: number[];
}
interface ServiceRow {
  serviceId: number;
  slug: string;
  name: string;
  url: string;
}

export function BoardDetailPage() {
  const { slug = "" } = useParams<{ slug: string }>();
  const qc = useQueryClient();
  const nav = useNavigate();
  const { data } = useQuery<BoardDetail>({
    queryKey: ["boards", slug],
    queryFn: () => api<BoardDetail>(`/api/admin/tenants/${slug}`),
  });
  const { data: services = [] } = useQuery<ServiceRow[]>({
    queryKey: ["services"],
    queryFn: () => api<ServiceRow[]>("/api/admin/services"),
  });
  const [selected, setSelected] = useState<number[]>([]);
  const [initialSelected, setInitialSelected] = useState<number[]>([]);
  const [host, setHost] = useState("");
  const [initialHost, setInitialHost] = useState("");
  const [confirmOpen, setConfirmOpen] = useState(false);

  useEffect(() => {
    if (data) {
      setSelected(data.serviceIds);
      setInitialSelected(data.serviceIds);
      setHost(data.board.boardHost ?? "");
      setInitialHost(data.board.boardHost ?? "");
    }
  }, [data]);

  const dirty =
    selected.length !== initialSelected.length ||
    selected.some((id) => !initialSelected.includes(id));

  const save = useMutation({
    mutationFn: () =>
      api(`/api/admin/tenants/${slug}`, {
        method: "PATCH",
        body: JSON.stringify({ serviceIds: selected }),
      }),
    onSuccess: async () => {
      setInitialSelected(selected);
      await qc.invalidateQueries({ queryKey: ["boards", slug] });
    },
  });

  const hostDirty = host.trim().toLowerCase() !== initialHost;
  const saveHost = useMutation({
    mutationFn: () =>
      api(`/api/admin/tenants/${slug}`, {
        method: "PATCH",
        // Empty string clears the hostname; the API lowercases and validates.
        body: JSON.stringify({ host: host.trim() }),
      }),
    onSuccess: async () => {
      setInitialHost(host.trim().toLowerCase());
      await qc.invalidateQueries({ queryKey: ["boards", slug] });
    },
  });

  const remove = useMutation({
    mutationFn: () => api(`/api/admin/tenants/${slug}`, { method: "DELETE" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["boards"] });
      nav("/admin/tenants");
    },
  });

  if (!data) return <div className="text-muted-foreground p-8">Loading…</div>;
  return (
    <div className="max-w-3xl space-y-4 p-6">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">{data.board.boardName}</h1>
          <a
            href={`/tenants/${data.board.boardSlug}`}
            target="_blank"
            rel="noreferrer"
            className="text-primary text-sm hover:underline"
          >
            /tenants/{data.board.boardSlug}
          </a>
        </div>
      </div>
      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Services on this tenant</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {services.map((m) => {
            const checked = selected.includes(m.serviceId);
            return (
              <label key={m.serviceId} className="flex cursor-pointer items-center gap-2 text-sm">
                <Checkbox
                  checked={checked}
                  onCheckedChange={(v) => {
                    setSelected((s) =>
                      v ? [...s, m.serviceId] : s.filter((x) => x !== m.serviceId),
                    );
                  }}
                />
                <span>{m.name}</span>
                <span className="text-muted-foreground text-xs">{m.url}</span>
              </label>
            );
          })}
          {dirty && (
            <div className="flex justify-end gap-2 pt-3">
              <Button
                variant="outline"
                onClick={() => setSelected(initialSelected)}
                disabled={save.isPending}
              >
                Cancel
              </Button>
              <Button onClick={() => save.mutate()} disabled={save.isPending}>
                Save changes
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Custom domain</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          <p className="text-muted-foreground text-sm">
            Serve this tenant's status board at its own hostname instead of /tenants/
            {data.board.boardSlug}. Point a CNAME at this server, then enter the bare hostname
            here. Leave empty to disable.
          </p>
          <Input
            value={host}
            onChange={(e) => setHost(e.target.value)}
            placeholder="status.example.com"
            className="font-mono"
          />
          {saveHost.isError && (
            <p className="text-danger text-sm">Couldn't save the hostname. Check the format and try again.</p>
          )}
          {hostDirty && (
            <div className="flex justify-end gap-2 pt-1">
              <Button variant="outline" onClick={() => setHost(initialHost)} disabled={saveHost.isPending}>
                Cancel
              </Button>
              <Button onClick={() => saveHost.mutate()} disabled={saveHost.isPending}>
                Save changes
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      <DangerZone>
        <DangerZoneItem
          title="Delete tenant"
          description="Permanently delete this tenant. The public status page will stop working."
          action={
            <Button
              variant="destructive"
              onClick={() => setConfirmOpen(true)}
              disabled={remove.isPending}
            >
              Delete tenant
            </Button>
          }
        />
      </DangerZone>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title="Delete tenant?"
        description={`"${data.board.boardName}" will be removed permanently. This cannot be undone.`}
        confirmLabel="Delete tenant"
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
