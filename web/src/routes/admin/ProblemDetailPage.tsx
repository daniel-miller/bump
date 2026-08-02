import { useParams, useNavigate } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api } from "@/lib/api";
import { formatAbsolute } from "@/lib/dates";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

interface ExceptionInfo {
  type?: string;
  value?: string;
  stackTrace?: string;
  innerExceptions?: ExceptionInfo[] | null;
}

interface ProblemRecord {
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
  extensions: string | null;
  appSlug: string;
  appName: string;
  appVersion: string;
  environment: string;
  environmentName: string;
  environmentDescription: string | null;
  exception: ExceptionInfo | null;
  userId: string | null;
  userEmail: string | null;
}

function Row({ label, value, mono }: { label: string; value: React.ReactNode; mono?: boolean }) {
  return (
    <div className="grid grid-cols-[10rem_1fr] gap-3 border-b py-1.5 last:border-b-0">
      <div className="text-muted-foreground pt-0.5 text-xs tracking-wide uppercase">{label}</div>
      <div className={mono ? "font-mono text-sm break-all" : "text-sm"}>
        {value ?? <span className="text-muted-foreground italic">none</span>}
      </div>
    </div>
  );
}

type ContextPart = { text: string | null | undefined; mono?: boolean; muted?: boolean };

function ContextRow({ label, parts }: { label: string; parts: ContextPart[] }) {
  const visible = parts.filter((p) => p.text != null && p.text !== "");
  return (
    <div className="grid grid-cols-[10rem_1fr] gap-3 border-b py-1.5 last:border-b-0">
      <div className="text-muted-foreground pt-0.5 text-xs tracking-wide uppercase">{label}</div>
      <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 text-sm">
        {visible.length === 0 && <span className="text-muted-foreground italic">none</span>}
        {visible.map((p, i) => (
          <span key={i} className="contents">
            {i > 0 && <span className="text-muted-foreground/50">·</span>}
            <span
              className={[
                p.mono ? "font-mono break-all" : "",
                p.muted ? "text-muted-foreground" : "",
              ].join(" ")}
            >
              {p.text}
            </span>
          </span>
        ))}
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card>
      <CardContent className="space-y-1 p-4">
        <h2 className="text-muted-foreground mb-2 text-sm font-semibold tracking-wide uppercase">
          {title}
        </h2>
        {children}
      </CardContent>
    </Card>
  );
}

function ExceptionBlock({ ex, depth = 0 }: { ex: ExceptionInfo; depth?: number }) {
  return (
    <div className={depth > 0 ? "border-muted mt-3 border-l-2 pl-3" : ""}>
      <div className="text-muted-foreground text-xs tracking-wide uppercase">
        {depth === 0 ? "Outer" : `Inner #${depth}`}
      </div>
      <div className="font-mono text-sm font-medium">{ex.type ?? "(unknown type)"}</div>
      {ex.value && <div className="mt-1 text-sm">{ex.value}</div>}
      {ex.stackTrace && (
        <pre className="bg-muted/40 mt-2 overflow-x-auto rounded-md p-2 font-mono text-xs whitespace-pre-wrap">
          {ex.stackTrace}
        </pre>
      )}
      {ex.innerExceptions?.map((inner, i) => (
        <ExceptionBlock key={i} ex={inner} depth={depth + 1} />
      ))}
    </div>
  );
}

function fetchMarkdown(problemKey: number) {
  return api<string>(`/api/problems/${problemKey}`, {
    headers: { Accept: "text/markdown" },
  });
}

async function downloadMarkdown(problemKey: number) {
  const md = await fetchMarkdown(problemKey);
  const blob = new Blob([md], { type: "text/markdown;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `problem-${problemKey}.md`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

async function copyMarkdown(problemKey: number) {
  const md = await fetchMarkdown(problemKey);
  await navigator.clipboard.writeText(md);
}

function parseExtensions(raw: string | null): Array<[string, string]> | null {
  if (!raw) return null;
  try {
    const obj = JSON.parse(raw);
    if (obj === null || typeof obj !== "object" || Array.isArray(obj)) return null;
    return Object.entries(obj as Record<string, unknown>).map(([k, v]) => [
      k,
      typeof v === "string" ? v : JSON.stringify(v),
    ]);
  } catch {
    return null;
  }
}

function ExtensionsTable({ rows }: { rows: Array<[string, string]> }) {
  return (
    <table className="w-full border-collapse text-sm">
      <thead>
        <tr className="text-muted-foreground text-xs tracking-wide uppercase">
          <th className="w-40 border-b py-1.5 pr-3 text-left font-medium">Key</th>
          <th className="border-b py-1.5 text-left font-medium">Value</th>
        </tr>
      </thead>
      <tbody>
        {rows.map(([k, v]) => (
          <tr key={k} className="border-b last:border-b-0">
            <td className="text-muted-foreground py-1.5 pr-3 align-top font-mono">{k}</td>
            <td className="py-1.5 font-mono break-all">{v}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export function ProblemDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const { data, isLoading, error } = useQuery<ProblemRecord>({
    queryKey: ["problems", id],
    queryFn: () => api<ProblemRecord>(`/api/problems/${id}`),
  });

  const [copied, setCopied] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [resolveBusy, setResolveBusy] = useState(false);
  const navigate = useNavigate();
  const qc = useQueryClient();

  if (isLoading) {
    return (
      <div className="p-6">
        <div className="text-muted-foreground">Loading...</div>
      </div>
    );
  }
  if (error || !data) {
    return (
      <div className="p-6">
        <div className="text-danger text-sm">
          Failed to load problem #{id}: {(error as Error)?.message ?? "not found"}
        </div>
      </div>
    );
  }

  const extensionRows = parseExtensions(data.extensions);

  return (
    <div className="max-w-4xl space-y-4 p-6">
      <div className="flex items-baseline justify-between gap-3">
        <h1 className="flex items-baseline gap-2 text-2xl font-semibold">
          Problem #{data.problemKey}
          {data.resolvedAt && (
            <span className="bg-success/15 text-success rounded-md px-2 py-0.5 text-xs font-medium">
              Resolved
            </span>
          )}
        </h1>
        <div className="flex items-center gap-2">
          <span className="text-muted-foreground font-mono text-xs">{data.fingerprint}</span>
          {data.resolvedAt ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={resolveBusy}
              onClick={async () => {
                setResolveBusy(true);
                try {
                  await api<void>(`/api/problems/${data.problemKey}/unresolve`, { method: "POST" });
                  await qc.invalidateQueries({ queryKey: ["problems"] });
                  await qc.invalidateQueries({ queryKey: ["problems", id] });
                } finally {
                  setResolveBusy(false);
                }
              }}
            >
              <i className="fa-sharp fa-regular fa-rotate-left" aria-hidden="true" />
              {resolveBusy ? "Unresolving..." : "Unresolve"}
            </Button>
          ) : (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={resolveBusy}
              onClick={async () => {
                setResolveBusy(true);
                try {
                  await api<void>(`/api/problems/${data.problemKey}/resolve`, { method: "POST" });
                  await qc.invalidateQueries({ queryKey: ["problems"] });
                  await qc.invalidateQueries({ queryKey: ["problems", id] });
                } finally {
                  setResolveBusy(false);
                }
              }}
            >
              <i className="fa-sharp fa-regular fa-check" aria-hidden="true" />
              {resolveBusy ? "Resolving..." : "Resolve"}
            </Button>
          )}
          <Button
            type="button"
            variant="ghost"
            size="icon"
            aria-label="Download as Markdown"
            title="Download as Markdown"
            onClick={() => downloadMarkdown(data.problemKey)}
          >
            <i className="fa-sharp fa-regular fa-download" aria-hidden="true" />
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            aria-label="Copy Markdown to clipboard"
            title={copied ? "Copied!" : "Copy Markdown to clipboard"}
            onClick={async () => {
              await copyMarkdown(data.problemKey);
              setCopied(true);
              setTimeout(() => setCopied(false), 1500);
            }}
          >
            <i
              className={`fa-sharp fa-regular fa-copy ${copied ? "text-success" : ""}`}
              aria-hidden="true"
            />
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            aria-label="Delete problem"
            title="Delete problem"
            onClick={() => {
              setDeleteError(null);
              setConfirmOpen(true);
            }}
          >
            <i className="fa-sharp fa-regular fa-trash-can text-danger" aria-hidden="true" />
          </Button>
        </div>
      </div>

      <Dialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete problem #{data.problemKey}?</DialogTitle>
            <DialogDescription>
              This permanently deletes the problem record. This can't be undone.
            </DialogDescription>
          </DialogHeader>
          {deleteError && <div className="text-danger text-sm">{deleteError}</div>}
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmOpen(false)} disabled={deleting}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              disabled={deleting}
              onClick={async () => {
                setDeleting(true);
                setDeleteError(null);
                try {
                  await api<void>(`/api/problems/${data.problemKey}`, { method: "DELETE" });
                  await qc.invalidateQueries({ queryKey: ["problems"] });
                  navigate("/admin/problems");
                } catch (e) {
                  setDeleteError((e as Error).message || "Delete failed.");
                  setDeleting(false);
                }
              }}
            >
              {deleting ? "Deleting..." : "Delete"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Section title="Problem">
        <Row label="Type" value={data.type} mono />
        <Row label="Title" value={data.title} />
        <Row label="Status" value={data.status ?? null} mono />
        <Row label="Detail" value={data.detail} />
        <Row label="Instance" value={data.instance} mono />
        <Row label="Fingerprint" value={data.fingerprint} mono />
        <Row label="Reported at" value={formatAbsolute(data.reportedAt)} />
        <Row
          label="Dispatched at"
          value={data.dispatchedAt ? formatAbsolute(data.dispatchedAt) : null}
        />
        <Row label="Resolved at" value={data.resolvedAt ? formatAbsolute(data.resolvedAt) : null} />
        <Row
          label="Extensions"
          value={
            extensionRows && extensionRows.length > 0 ? (
              <ExtensionsTable rows={extensionRows} />
            ) : null
          }
        />
      </Section>

      <Section title="Context">
        <ContextRow
          label="App"
          parts={[
            { mono: true, text: data.appSlug },
            { text: data.appName },
            { mono: true, text: `v${data.appVersion}` },
          ]}
        />
        <ContextRow
          label="Environment"
          parts={[
            { mono: true, text: data.environment },
            { text: data.environmentName },
            { text: data.environmentDescription, muted: true },
          ]}
        />
        <ContextRow
          label="Account"
          parts={[{ mono: true, text: data.userId }, { text: data.userEmail }]}
        />
      </Section>

      <Section title="Exception">
        {data.exception ? (
          <ExceptionBlock ex={data.exception} />
        ) : (
          <div className="text-muted-foreground text-sm italic">No exception attached.</div>
        )}
      </Section>

      <p className="text-muted-foreground pt-2 text-xs">
        Problem reports follow{" "}
        <a
          href="https://datatracker.ietf.org/doc/html/rfc7807"
          target="_blank"
          rel="noopener noreferrer"
          className="hover:text-foreground underline"
        >
          RFC 7807
        </a>
        .
      </p>
    </div>
  );
}
