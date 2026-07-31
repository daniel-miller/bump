import { useParams } from "react-router-dom";
import { AlertTriangle, Info, Wrench } from "lucide-react";
import { useStatus } from "@/hooks/useStatus";
import { StatusDot } from "@/components/StatusDot";
import { ServiceCard } from "@/components/ServiceCard";
import { TrendBars } from "@/components/TrendBars";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

const announcementStyles = {
  info: {
    icon: Info,
    bg: "bg-primary/10",
    border: "border-primary/40",
    iconColor: "text-primary",
  },
  warning: {
    icon: AlertTriangle,
    bg: "bg-warning/10",
    border: "border-warning/50",
    iconColor: "text-warning",
  },
  maintenance: {
    icon: Wrench,
    bg: "bg-muted",
    border: "border-border",
    iconColor: "text-muted-foreground",
  },
} as const;

export function BoardPage({ slug: slugProp }: { slug?: string } = {}) {
  // Slug comes from the route param (/tenants/:slug) or as a prop when the
  // HostGate resolves a custom hostname to a tenant at the site root.
  const { slug: slugParam } = useParams<{ slug: string }>();
  const slug = slugProp ?? slugParam;
  const { data, isLoading, error } = useStatus(slug);

  if (isLoading) return <div className="text-muted-foreground p-8">Loading…</div>;
  if (error || !data)
    return <div className="text-danger p-8">Couldn't load status. Try refreshing the page.</div>;

  const overallLabel =
    data.overall === "operational"
      ? "All systems operational"
      : data.overall === "degraded"
        ? "Some services degraded"
        : data.overall === "down"
          ? "Major outage"
          : "Status unknown";

  return (
    <div className="mx-auto max-w-5xl space-y-8 p-6">
      <header className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">{data.board?.name ?? "Status"}</h1>
        <div className="flex items-center gap-2">
          <StatusDot status={data.overall} size={14} />
          <span className="text-lg">{overallLabel}</span>
        </div>
      </header>

      {data.announcements.length > 0 && (
        <div className="space-y-2">
          {data.announcements.map((a) => {
            const style = announcementStyles[a.type] ?? announcementStyles.info;
            const Icon = style.icon;
            return (
              <div
                key={a.id}
                className={`flex gap-3 rounded border p-3 ${style.bg} ${style.border}`}
              >
                <Icon className={`mt-0.5 h-5 w-5 shrink-0 ${style.iconColor}`} />
                <div className="min-w-0">
                  <div className="text-muted-foreground text-sm font-semibold capitalize">
                    {a.type}
                  </div>
                  <div className="text-sm font-semibold">{a.title}</div>
                  <div className="text-muted-foreground text-sm whitespace-pre-wrap">
                    {a.content}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {(["operational", "uptime", "latency", "outages"] as const).map((k) => {
          const kpi = data.kpis[k];
          return (
            <Card key={k}>
              <CardContent className="p-4">
                <div className="text-muted-foreground text-xs">{kpi?.label ?? "—"}</div>
                <div className="mt-1 text-2xl font-semibold">{kpi?.value ?? "—"}</div>
                <div className="text-muted-foreground mt-1 text-xs">{kpi?.subtitle ?? ""}</div>
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

      <section className="space-y-3">
        {data.services.map((m) => (
          <ServiceCard
            key={m.slug}
            service={{
              name: m.name,
              url: m.url,
              tenant: m.tenant,
              environment: m.environment,
              paused: m.paused,
              status: m.status,
              latencyMs: m.latencyMs,
              uptime: m.uptime,
              history: m.history,
            }}
          />
        ))}
      </section>

      {data.outages.length > 0 && (
        <section className="space-y-2">
          <div className="text-sm font-semibold">Active outages</div>
          {data.outages.map((i) => (
            <Card key={i.id}>
              <CardContent className="p-3">
                <div className="font-medium">{i.title}</div>
                <div className="text-muted-foreground text-xs">
                  Started {new Date(i.startedAt).toLocaleString()} · {i.status}
                </div>
              </CardContent>
            </Card>
          ))}
        </section>
      )}
    </div>
  );
}
