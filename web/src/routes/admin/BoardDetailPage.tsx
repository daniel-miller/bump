import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DangerZone, DangerZoneItem } from "@/components/ui/danger-zone";

interface BoardDetail {
  board: { boardId: number; boardSlug: string; boardName: string };
  serviceIds: number[];
}
interface ServiceRow { serviceId: number; slug: string; name: string; url: string }

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
  const [confirmOpen, setConfirmOpen] = useState(false);

  useEffect(() => {
    if (data) {
      setSelected(data.serviceIds);
      setInitialSelected(data.serviceIds);
    }
  }, [data]);

  const dirty =
    selected.length !== initialSelected.length ||
    selected.some((id) => !initialSelected.includes(id));

  const save = useMutation({
    mutationFn: () => api(`/api/admin/tenants/${slug}`, { method: "PATCH", body: JSON.stringify({ serviceIds: selected }) }),
    onSuccess: async () => {
      setInitialSelected(selected);
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

  if (!data) return <div className="p-8 text-muted-foreground">Loading…</div>;
  return (
    <div className="p-6 space-y-4 max-w-3xl">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">{data.board.boardName}</h1>
          <a
            href={`/tenants/${data.board.boardSlug}`}
            target="_blank"
            rel="noreferrer"
            className="text-sm text-primary hover:underline"
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
              <label key={m.serviceId} className="flex items-center gap-2 text-sm cursor-pointer">
                <Checkbox
                  checked={checked}
                  onCheckedChange={(v) => {
                    setSelected((s) => v ? [...s, m.serviceId] : s.filter((x) => x !== m.serviceId));
                  }}
                />
                <span>{m.name}</span>
                <span className="text-xs text-muted-foreground">{m.url}</span>
              </label>
            );
          })}
          {dirty && (
            <div className="flex justify-end gap-2 pt-3">
              <Button variant="outline" onClick={() => setSelected(initialSelected)} disabled={save.isPending}>
                Cancel
              </Button>
              <Button onClick={() => save.mutate()} disabled={save.isPending}>
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
