import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";

interface BoardRow { boardId: number; boardSlug: string; boardName: string }

export function BoardsListPage() {
  const qc = useQueryClient();
  const { data = [], isLoading } = useQuery<BoardRow[]>({ queryKey: ["boards"], queryFn: () => api<BoardRow[]>("/api/admin/tenants") });
  const [showNew, setShowNew] = useState(false);
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const create = useMutation({
    mutationFn: () => api("/api/admin/tenants", { method: "POST", body: JSON.stringify({ slug, name }) }),
    onSuccess: async () => {
      setShowNew(false);
      setSlug("");
      setName("");
      await qc.invalidateQueries({ queryKey: ["boards"] });
    },
  });
  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Tenants</h1>
        <Button onClick={() => setShowNew(true)}>New tenant</Button>
      </div>
      {showNew && (
        <Card>
          <CardContent className="space-y-3 p-4">
            <div className="grid grid-cols-3 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="tenant-slug">Slug</Label>
                <Input id="tenant-slug" placeholder="acme-corp" value={slug} onChange={(e) => setSlug(e.target.value)} />
              </div>
              <div className="space-y-1.5 col-span-2">
                <Label htmlFor="tenant-name">Name</Label>
                <Input id="tenant-name" placeholder="Acme Corp" value={name} onChange={(e) => setName(e.target.value)} />
              </div>
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => { setShowNew(false); setSlug(""); setName(""); }}>Cancel</Button>
              <Button onClick={() => create.mutate()} disabled={!slug || !name || create.isPending}>
                Create tenant
              </Button>
            </div>
          </CardContent>
        </Card>
      )}
      <div className="space-y-2">
        {data.map((b) => (
          <Link to={`/admin/tenants/${b.boardSlug}`} key={b.boardId} className="block">
            <Card className="hover:border-primary transition-colors">
              <CardContent className="p-3">
                <div className="font-medium">{b.boardName}</div>
                <div className="text-xs text-muted-foreground">/{b.boardSlug}</div>
              </CardContent>
            </Card>
          </Link>
        ))}
        {!isLoading && data.length === 0 && (
          <div className="text-sm text-muted-foreground">No tenants yet. Add one to host a public status page.</div>
        )}
      </div>
    </div>
  );
}
