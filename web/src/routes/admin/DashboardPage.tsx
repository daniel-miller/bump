import { useEffect, useState } from "react";
import { STATUS_REFETCH_INTERVAL_MS, useStatus } from "@/hooks/useStatus";
import { StatusDot } from "@/components/StatusDot";
import { TrendBars } from "@/components/TrendBars";
import { ProblemsBars } from "@/components/ProblemsBars";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function DashboardPage() {
  const { data, isLoading, dataUpdatedAt } = useStatus(undefined, { excludePaused: true });

  // Tick once per second so the countdown re-renders without forcing a refetch.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, []);

  if (isLoading || !data) return <div className="p-8 text-muted-foreground">Loading…</div>;

  const updatedAtLabel = new Date(data.updatedAt).toLocaleString(undefined, { timeZoneName: "short" });
  const secondsLeft = Math.max(0, Math.ceil((dataUpdatedAt + STATUS_REFETCH_INTERVAL_MS - now) / 1000));
  // Pad to two digits so the "10s → 9s" transition does not change layout width.
  const nextLabel = `next in ${String(secondsLeft).padStart(2, "0")}s`;

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center gap-3">
        <h1 className="text-2xl font-semibold">Dashboard</h1>
        <StatusDot status={data.overall} size={14} />
        <div className="ml-auto text-xs text-muted-foreground tabular-nums">
          Updated {updatedAtLabel} · {nextLabel}
        </div>
      </div>
      <section className="grid grid-cols-2 md:grid-cols-5 gap-3">
        {(["operational", "uptime", "latency", "outages", "problems"] as const).map((k) => {
          const kpi = data.kpis[k];
          return (
            <Card key={k}>
              <CardContent className="p-4">
                <div className="text-xs text-muted-foreground">{kpi?.label ?? "—"}</div>
                <div className="text-2xl font-semibold mt-1">{kpi?.value ?? "—"}</div>
                <div className="text-xs text-muted-foreground mt-1">{kpi?.subtitle ?? ""}</div>
              </CardContent>
            </Card>
          );
        })}
      </section>
      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Probes (14d)</CardTitle>
        </CardHeader>
        <CardContent>
          <TrendBars data={data.trend} />
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Problems (14d)</CardTitle>
        </CardHeader>
        <CardContent>
          <ProblemsBars data={data.problemsTrend} />
        </CardContent>
      </Card>
    </div>
  );
}
