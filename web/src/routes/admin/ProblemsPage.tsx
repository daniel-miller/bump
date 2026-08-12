import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatAbsolute } from "@/lib/dates";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";

interface ExceptionInfo {
  type?: string;
  value?: string;
  stackTrace?: string;
  innerExceptions?: ExceptionInfo[] | null;
}

interface ProblemRow {
  problemKey: number;
  fingerprint: string;
  reportedAt: string;
  dispatchedAt: string | null;
  resolvedAt: string | null;
  type: string;
  title: string;
  status: number | null;
  detail: string | null;
  instance: string | null;
  environment: string;
  appHandle: string;
  exception: ExceptionInfo | null;
  userEmail: string | null;
}

function statusTone(status: number | null): string {
  if (status === null) return "bg-muted text-muted-foreground";
  if (status >= 500) return "bg-danger/15 text-danger";
  if (status >= 400) return "bg-warning/15 text-warning";
  return "bg-muted text-muted-foreground";
}

export function ProblemsPage() {
  const qc = useQueryClient();
  const [includeResolved, setIncludeResolved] = useState(false);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const {
    data = [],
    isLoading,
    error,
  } = useQuery<ProblemRow[]>({
    queryKey: ["problems", { includeResolved }],
    queryFn: () => api<ProblemRow[]>(`/api/problems?includeResolved=${includeResolved}`),
  });

  // Only rows currently on screen count as selected. Toggling "show resolved"
  // can hide a row the user had ticked, and deleting something they can no
  // longer see would be a nasty surprise.
  const selectedKeys = data.filter((p) => selected.has(p.problemKey)).map((p) => p.problemKey);
  const allSelected = data.length > 0 && selectedKeys.length === data.length;

  function toggleRow(problemKey: number, checked: boolean) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (checked) next.add(problemKey);
      else next.delete(problemKey);
      return next;
    });
  }

  function toggleAll(checked: boolean) {
    setSelected(checked ? new Set(data.map((p) => p.problemKey)) : new Set());
  }

  const remove = useMutation({
    mutationFn: (problemKeys: number[]) =>
      api<{ deleted: number }>("/api/problems/delete", {
        method: "POST",
        body: JSON.stringify({ problemKeys }),
      }),
    onSuccess: async () => {
      setConfirmOpen(false);
      setSelected(new Set());
      setDeleteError(null);
      await qc.invalidateQueries({ queryKey: ["problems"] });
    },
    onError: (err: Error) => setDeleteError(err.message || "Delete failed."),
  });

  return (
    <div className="space-y-4 p-6">
      <div className="flex items-baseline justify-between gap-3">
        <h1 className="text-2xl font-semibold">Problems</h1>
        <div className="flex items-center gap-4">
          <label className="flex cursor-pointer items-center gap-2 text-sm select-none">
            <Checkbox
              checked={includeResolved}
              onCheckedChange={(v) => setIncludeResolved(v === true)}
            />
            Show resolved
          </label>
          {!isLoading && data.length > 0 && (
            <span className="text-muted-foreground text-sm">
              {data.length} report{data.length === 1 ? "" : "s"}
            </span>
          )}
        </div>
      </div>

      {isLoading && <div className="text-muted-foreground">Loading...</div>}
      {error && (
        <div className="text-danger text-sm">
          Failed to load problems: {(error as Error).message}
        </div>
      )}

      {data.length > 0 && (
        <div className="flex items-center justify-between gap-3 border-b pb-2">
          <label className="flex cursor-pointer items-center gap-2 text-sm select-none">
            <Checkbox
              checked={allSelected ? true : selectedKeys.length > 0 ? "indeterminate" : false}
              onCheckedChange={(v) => toggleAll(v === true)}
              aria-label="Select all problems"
            />
            {selectedKeys.length > 0 ? `${selectedKeys.length} selected` : "Select all"}
          </label>
          {selectedKeys.length > 0 && (
            <Button
              type="button"
              variant="destructive"
              size="sm"
              disabled={remove.isPending}
              onClick={() => {
                setDeleteError(null);
                setConfirmOpen(true);
              }}
            >
              <i className="fa-sharp fa-regular fa-trash-can" aria-hidden="true" />
              Delete selected
            </Button>
          )}
        </div>
      )}

      {deleteError && <div className="text-danger text-sm">{deleteError}</div>}

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={(open) => {
          if (remove.isPending) return;
          setConfirmOpen(open);
        }}
        title={`Delete ${selectedKeys.length} problem${selectedKeys.length === 1 ? "" : "s"}?`}
        description="This permanently deletes the selected problem records. This can't be undone."
        confirmLabel={remove.isPending ? "Deleting..." : "Delete"}
        variant="danger"
        disabled={remove.isPending}
        onConfirm={() => remove.mutate(selectedKeys)}
      />

      <div className="space-y-2">
        {data.map((p) => {
          const exceptionType = p.exception?.type;
          const innerCount = p.exception?.innerExceptions?.length ?? 0;
          return (
            <Card key={p.problemKey} className="hover:border-primary transition-colors">
              <CardContent className="flex items-start gap-3 p-3">
                <Checkbox
                  className="mt-1"
                  checked={selected.has(p.problemKey)}
                  onCheckedChange={(v) => toggleRow(p.problemKey, v === true)}
                  aria-label={`Select problem ${p.problemKey}`}
                />
                <Link
                  to={`/problems/${p.problemKey}`}
                  className="flex min-w-0 flex-1 items-start justify-between gap-3"
                >
                  <div className="min-w-0 flex-1 space-y-1">
                    <div className="flex flex-wrap items-center gap-2">
                      {p.status !== null && (
                        <span
                          className={`rounded-md px-1.5 py-0.5 font-mono text-xs ${statusTone(p.status)}`}
                        >
                          {p.status}
                        </span>
                      )}
                      <span className="truncate font-medium">
                        {p.title || p.type || "(untitled)"}
                      </span>
                    </div>
                    {exceptionType && (
                      <div className="text-muted-foreground truncate text-xs">
                        <span className="font-mono">{exceptionType}</span>
                        {innerCount > 0 && <span> · {innerCount} inner</span>}
                      </div>
                    )}
                    <div className="text-muted-foreground flex flex-wrap gap-x-2 text-xs">
                      <span className="font-mono">
                        {p.appHandle}/{p.environment}
                      </span>
                      {p.instance && <span className="truncate">{p.instance}</span>}
                      <span className="font-mono opacity-60">{p.fingerprint}</span>
                    </div>
                  </div>
                  <div className="text-muted-foreground text-right text-xs whitespace-nowrap">
                    <div>{formatAbsolute(p.reportedAt)}</div>
                    {p.resolvedAt && <div className="text-success">Resolved</div>}
                    {p.userEmail && <div className="opacity-70">{p.userEmail}</div>}
                  </div>
                </Link>
              </CardContent>
            </Card>
          );
        })}
        {!isLoading && data.length === 0 && (
          <div className="text-muted-foreground text-sm">No problems detected.</div>
        )}
      </div>
    </div>
  );
}
