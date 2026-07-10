import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

interface AppRecord {
  appKey: number;
  appSlug: string;
  appName: string;
  version: string;
  createdAt: string;
  updatedAt: string | null;
}

interface ProblemDetail {
  title?: string;
  detail?: string;
}

export function AppsPage() {
  const qc = useQueryClient();
  const {
    data = [],
    isLoading,
    isError,
  } = useQuery<AppRecord[]>({
    queryKey: ["admin", "apps"],
    queryFn: () => api<AppRecord[]>("/api/admin/apps"),
  });

  const [editing, setEditing] = useState<AppRecord | null>(null);

  return (
    <div className="space-y-4 p-6">
      <h1 className="text-2xl font-semibold">Apps</h1>
      {isLoading && <div className="text-muted-foreground text-sm">Loading…</div>}
      {isError && (
        <div className="text-danger text-sm">Couldn't load apps. Try refreshing the page.</div>
      )}
      <div className="space-y-2">
        {data.map((a) => (
          <Card key={a.appKey}>
            <CardContent className="flex items-center justify-between p-3">
              <div>
                <div className="font-medium">{a.appName}</div>
                <div className="text-muted-foreground text-xs">{a.appSlug}</div>
              </div>
              <div className="flex items-center gap-3">
                <span className="font-mono text-sm">{a.version}</span>
                <Button variant="outline" size="sm" onClick={() => setEditing(a)}>
                  Edit
                </Button>
              </div>
            </CardContent>
          </Card>
        ))}
        {!isLoading && data.length === 0 && (
          <div className="text-muted-foreground text-sm">No apps registered yet.</div>
        )}
      </div>

      <EditAppDialog
        app={editing}
        onClose={() => setEditing(null)}
        onSaved={async () => {
          setEditing(null);
          await qc.invalidateQueries({ queryKey: ["admin", "apps"] });
        }}
      />
    </div>
  );
}

function parseVersion(version: string): [number, number, number] {
  const parts = version.split(".").map((p) => parseInt(p, 10));
  return [parts[0] ?? 0, parts[1] ?? 0, parts[2] ?? 0];
}

interface EditAppDialogProps {
  app: AppRecord | null;
  onClose: () => void;
  onSaved: () => void;
}

function EditAppDialog({ app, onClose, onSaved }: EditAppDialogProps) {
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const [major, setMajor] = useState(0);
  const [minor, setMinor] = useState(0);
  const [patch, setPatch] = useState(0);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (app) {
      const [ma, mi, pa] = parseVersion(app.version);
      setSlug(app.appSlug);
      setName(app.appName);
      setMajor(ma);
      setMinor(mi);
      setPatch(pa);
      setError(null);
    }
  }, [app]);

  const save = useMutation({
    mutationFn: () => {
      if (!app) throw new Error("no app");
      return api<AppRecord>(`/api/admin/apps/${encodeURIComponent(app.appSlug)}`, {
        method: "PATCH",
        body: JSON.stringify({ slug, name, major, minor, patch }),
      });
    },
    onSuccess: () => onSaved(),
    onError: (err) => {
      if (err instanceof ApiError) {
        const problem = err.problem as ProblemDetail | undefined;
        setError(problem?.detail ?? problem?.title ?? err.message);
      } else {
        setError((err as Error).message);
      }
    },
  });

  return (
    <Dialog
      open={app !== null}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit app</DialogTitle>
          <DialogDescription>
            Changing the slug renames the app's URL. Existing SDK clients pinned to the old slug
            will need to be updated.
          </DialogDescription>
        </DialogHeader>

        <form
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            setError(null);
            save.mutate();
          }}
        >
          <div className="space-y-1">
            <Label htmlFor="edit-app-slug">Slug</Label>
            <Input
              id="edit-app-slug"
              value={slug}
              onChange={(e) => setSlug(e.target.value)}
              autoComplete="off"
              required
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="edit-app-name">Name</Label>
            <Input
              id="edit-app-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              autoComplete="off"
              required
            />
          </div>
          <div className="grid grid-cols-3 gap-2">
            <div className="space-y-1">
              <Label htmlFor="edit-app-major">Major</Label>
              <Input
                id="edit-app-major"
                type="number"
                min={0}
                value={major}
                onChange={(e) => setMajor(parseInt(e.target.value, 10) || 0)}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="edit-app-minor">Minor</Label>
              <Input
                id="edit-app-minor"
                type="number"
                min={0}
                value={minor}
                onChange={(e) => setMinor(parseInt(e.target.value, 10) || 0)}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="edit-app-patch">Patch</Label>
              <Input
                id="edit-app-patch"
                type="number"
                min={0}
                value={patch}
                onChange={(e) => setPatch(parseInt(e.target.value, 10) || 0)}
              />
            </div>
          </div>

          {error && <div className="text-danger text-sm">{error}</div>}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={save.isPending}>
              Cancel
            </Button>
            <Button type="submit" disabled={save.isPending}>
              {save.isPending ? "Saving…" : "Save changes"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
