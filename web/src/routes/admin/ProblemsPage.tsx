import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";

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
  appSlug: string;
  exception: ExceptionInfo | null;
  userEmail: string | null;
}

function statusTone(status: number | null): string {
  if (status === null) return "bg-muted text-muted-foreground";
  if (status >= 500) return "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200";
  if (status >= 400) return "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200";
  return "bg-muted text-muted-foreground";
}

export function ProblemsPage() {
  const [includeResolved, setIncludeResolved] = useState(false);
  const {
    data = [],
    isLoading,
    error,
  } = useQuery<ProblemRow[]>({
    queryKey: ["problems", { includeResolved }],
    queryFn: () => api<ProblemRow[]>(`/api/problems?includeResolved=${includeResolved}`),
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

      {isLoading && <div className="text-muted-foreground">Loading…</div>}
      {error && (
        <div className="text-sm text-red-600 dark:text-red-400">
          Failed to load problems: {(error as Error).message}
        </div>
      )}

      <div className="space-y-2">
        {data.map((p) => {
          const exceptionType = p.exception?.type;
          const innerCount = p.exception?.innerExceptions?.length ?? 0;
          return (
            <Link to={`/admin/problems/${p.problemKey}`} key={p.problemKey} className="block">
              <Card className="hover:border-primary transition-colors">
                <CardContent className="space-y-1 p-3">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        {p.status !== null && (
                          <span
                            className={`rounded px-1.5 py-0.5 font-mono text-xs ${statusTone(p.status)}`}
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
                          {p.appSlug}/{p.environment}
                        </span>
                        {p.instance && <span className="truncate">{p.instance}</span>}
                        <span className="font-mono opacity-60">{p.fingerprint}</span>
                      </div>
                    </div>
                    <div className="text-muted-foreground text-right text-xs whitespace-nowrap">
                      <div>{new Date(p.reportedAt).toLocaleString()}</div>
                      {p.resolvedAt && (
                        <div className="text-green-700 dark:text-green-400">Resolved</div>
                      )}
                      {p.userEmail && <div className="opacity-70">{p.userEmail}</div>}
                    </div>
                  </div>
                </CardContent>
              </Card>
            </Link>
          );
        })}
        {!isLoading && data.length === 0 && (
          <div className="text-muted-foreground text-sm">No problems detected.</div>
        )}
      </div>
    </div>
  );
}
