import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";

interface OwnerRow {
  ownerId: number;
  ownerNumber: number | null;
  ownerHandle: string;
  ownerName: string;
  ownerDescription: string | null;
  ownerHost: string | null;
}

export function OwnersListPage() {
  const qc = useQueryClient();
  const {
    data = [],
    isLoading,
    isError,
  } = useQuery<OwnerRow[]>({
    queryKey: ["owners"],
    queryFn: () => api<OwnerRow[]>("/api/admin/owners"),
  });
  const [showNew, setShowNew] = useState(false);
  const [handle, setHandle] = useState("");
  const [name, setName] = useState("");
  const create = useMutation({
    mutationFn: () =>
      api("/api/admin/owners", { method: "POST", body: JSON.stringify({ handle, name }) }),
    onSuccess: async () => {
      setShowNew(false);
      setHandle("");
      setName("");
      await qc.invalidateQueries({ queryKey: ["owners"] });
    },
  });
  return (
    <div className="space-y-4 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Owners</h1>
          <p className="text-muted-foreground mt-1 text-sm">
            The party each deployment belongs to; numbers and order follow the infra roster
          </p>
        </div>
        <Button onClick={() => setShowNew(true)}>New owner</Button>
      </div>
      {showNew && (
        <Card>
          <CardContent className="space-y-3 p-4">
            <div className="grid grid-cols-3 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="owner-handle">Handle</Label>
                <Input
                  id="owner-handle"
                  placeholder="acme-corp"
                  value={handle}
                  onChange={(e) => setHandle(e.target.value)}
                />
              </div>
              <div className="col-span-2 space-y-1.5">
                <Label htmlFor="owner-name">Name</Label>
                <Input
                  id="owner-name"
                  placeholder="Acme Corp"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                />
              </div>
            </div>
            <div className="flex justify-end gap-2">
              <Button
                variant="outline"
                onClick={() => {
                  setShowNew(false);
                  setHandle("");
                  setName("");
                }}
              >
                Cancel
              </Button>
              <Button onClick={() => create.mutate()} disabled={!handle || !name || create.isPending}>
                Create owner
              </Button>
            </div>
          </CardContent>
        </Card>
      )}
      {/* A failed fetch must never read as an empty roster. */}
      {isError && (
        <div className="text-danger text-sm">Couldn't load owners. Try refreshing the page.</div>
      )}
      <div className="space-y-2">
        {data.map((o) => (
          <Link to={`/owners/${o.ownerHandle}`} key={o.ownerId} className="block">
            <Card className="hover:border-primary transition-colors">
              <CardContent className="flex items-center gap-3 p-3">
                <span className="text-muted-foreground w-7 shrink-0 text-right font-mono text-sm">
                  {o.ownerNumber !== null ? String(o.ownerNumber).padStart(2, "0") : ""}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-baseline gap-2">
                    <span className="truncate font-medium">{o.ownerName}</span>
                    <span className="text-muted-foreground font-mono text-xs">{o.ownerHandle}</span>
                  </div>
                  {o.ownerDescription && (
                    <div className="text-muted-foreground truncate text-xs">
                      {o.ownerDescription}
                    </div>
                  )}
                </div>
                {o.ownerHost && (
                  <span className="text-muted-foreground hidden shrink-0 font-mono text-xs sm:inline">
                    {o.ownerHost}
                  </span>
                )}
              </CardContent>
            </Card>
          </Link>
        ))}
        {!isLoading && !isError && data.length === 0 && (
          <div className="text-muted-foreground text-sm">
            No owners yet. Add one to host a public status page.
          </div>
        )}
      </div>
    </div>
  );
}
