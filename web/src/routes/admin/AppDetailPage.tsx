import { Link, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { ApiError, api } from "@/lib/api";
import { formatAbsolute } from "@/lib/dates";
import { ServiceCard } from "@/components/ServiceCard";
import type { ServiceStatus } from "@/lib/types";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeader } from "@/components/ui/page-header";

interface AppRecord {
  appKey: number;
  appHandle: string;
  appName: string;
  // Omitted from the payload when null (the API writes with ignoreNulls).
  appDescription?: string;
  version: string;
  createdAt: string;
  updatedAt?: string;
}

interface ServiceRow {
  handle: string;
  name: string;
  url: string;
  owner: string;
  environment: string;
  app: string | null;
  paused: boolean;
  lastStatus: ServiceStatus;
  latencyMs: number;
  uptime: number;
  history: ServiceStatus[];
}

export function AppDetailPage() {
  const { handle = "" } = useParams<{ handle: string }>();
  const {
    data: app,
    isLoading,
    error,
  } = useQuery<AppRecord>({
    queryKey: ["admin", "apps", handle],
    queryFn: () => api<AppRecord>(`/api/admin/apps/${encodeURIComponent(handle)}`),
    enabled: !!handle,
    retry: (count, err) => !(err instanceof ApiError && err.status === 404) && count < 3,
  });
  // The services roster carries each service's app handle, so the app's
  // services are a filter over the same cached list the Services page uses.
  const {
    data: services = [],
    isLoading: servicesLoading,
    isError: servicesError,
  } = useQuery<ServiceRow[]>({
    queryKey: ["services"],
    queryFn: () => api<ServiceRow[]>("/api/admin/services"),
  });

  if (isLoading) return <div className="text-muted-foreground p-8">Loading...</div>;
  if (error instanceof ApiError && error.status === 404) {
    return <div className="text-muted-foreground p-8">No app has the handle "{handle}".</div>;
  }
  if (error || !app) {
    return (
      <div className="text-danger p-8 text-sm">Couldn't load the app. Try refreshing the page.</div>
    );
  }

  const linked = services.filter((s) => s.app === app.appHandle);
  const fields: [string, React.ReactNode][] = [
    ["Name", app.appName],
    ["Handle", <span className="font-mono">{app.appHandle}</span>],
    ["Description", app.appDescription ?? <span className="text-muted-foreground">None</span>],
    ["Version", <span className="font-mono">{app.version}</span>],
    ["App key", <span className="font-mono">{app.appKey}</span>],
    ["Created", formatAbsolute(app.createdAt)],
    [
      "Updated",
      app.updatedAt ? (
        formatAbsolute(app.updatedAt)
      ) : (
        <span className="text-muted-foreground">Never</span>
      ),
    ],
  ];

  return (
    <div className="space-y-4 p-6">
      <PageHeader
        title={app.appName}
        subtitle={<span className="font-mono">{app.appHandle}</span>}
      />

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Details</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {fields.map(([label, value]) => (
            <div key={label} className="flex items-baseline gap-3 text-sm">
              <span className="text-muted-foreground w-36 shrink-0 text-xs">{label}</span>
              <span className="min-w-0 break-words">{value}</span>
            </div>
          ))}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Services</CardTitle>
        </CardHeader>
        <CardContent>
          {servicesLoading && <div className="text-muted-foreground text-sm">Loading...</div>}
          {/* A failed fetch must never read as an app with no services. */}
          {servicesError && (
            <div className="text-danger text-sm">
              Couldn't load services. Try refreshing the page.
            </div>
          )}
          {!servicesLoading && !servicesError && linked.length === 0 && (
            <div className="text-muted-foreground text-sm">No services linked to this app</div>
          )}
          <div className="grid grid-cols-1 gap-2 lg:grid-cols-2">
            {linked.map((s) => (
              <Link to={`/services/${s.handle}`} key={s.handle} className="block">
                <ServiceCard
                  interactive
                  service={{
                    name: s.name,
                    url: s.url,
                    handle: s.handle,
                    owner: s.owner,
                    environment: s.environment,
                    paused: s.paused,
                    status: s.lastStatus,
                    latencyMs: s.latencyMs,
                    uptime: s.uptime,
                    history: s.history,
                  }}
                />
              </Link>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
