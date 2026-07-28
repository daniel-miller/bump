import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api";
import { useQuery } from "@tanstack/react-query";
import { ExternalLink } from "lucide-react";

interface RepositoryInfo {
  url: string;
}

interface BuildInfo {
  version: string;
  assemblyVersion: string;
  informationalVersion: string | null;
  fileVersion: string | null;
  buildDate: string | null;
}

interface ServerInfo {
  machineName: string;
  releaseEnvironment: string;
  environmentName: string;
  processId: number;
  processStartedAt: string;
  processUptime: string;
  framework: string;
  operatingSystem: string;
  architecture: string;
  processorCount: number;
  workingSetBytes: number;
  gcTotalMemoryBytes: number;
  serverTime: string;
  timezone: string;
}

interface DatabaseInfo {
  version: string;
  serverTime: string;
  host: string | null;
  databaseName: string | null;
  latestMigration: string | null;
  latestMigrationAt: string | null;
  migrationCount: number;
}

interface RegistryInfo {
  apps: number;
  environments: number;
  specialEnvironments: number;
  tenants: number;
  services: number;
  servicesPaused: number;
  openOutages: number;
  outages7d: number;
  openProblemsDistinct24h: number;
  problemReports24h: number;
  problemReports7d: number;
  accounts: number;
  activeAnnouncements: number;
  probeEvents24h: number;
}

interface AboutResponse {
  repository: RepositoryInfo;
  build: BuildInfo;
  server: ServerInfo;
  database: DatabaseInfo;
  registry: RegistryInfo;
}

interface VersionResponse {
  version: string;
  major: number;
  minor: number;
  patch: number;
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  const units = ["KiB", "MiB", "GiB", "TiB"];
  let v = n / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(1)} ${units[i]}`;
}

function formatDate(s: string | null): string {
  if (!s) return "—";
  return new Date(s).toLocaleString(undefined, { timeZoneName: "short" });
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[10rem_1fr] gap-3 py-1 text-sm">
      <div className="text-muted-foreground">{label}</div>
      <div className="font-mono break-all">{children}</div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: number | string }) {
  return (
    <div className="border-border rounded border p-3">
      <div className="text-muted-foreground text-xs">{label}</div>
      <div className="mt-1 text-2xl font-semibold tabular-nums">{value}</div>
    </div>
  );
}

export function AboutPage() {
  const { data, isLoading, isError } = useQuery<AboutResponse>({
    queryKey: ["admin", "about"],
    queryFn: () => api<AboutResponse>("/api/admin/about"),
  });
  const { data: ver } = useQuery<VersionResponse>({
    queryKey: ["version"],
    queryFn: () => api<VersionResponse>("/api/version"),
  });

  if (isLoading) return <div className="text-muted-foreground p-8">Loading…</div>;
  if (isError || !data)
    return (
      <div className="text-danger p-8">Couldn't load about info. Try refreshing the page.</div>
    );

  const { repository, build, server, database, registry } = data;
  const displayVersion = ver?.version ?? build.version;

  return (
    <div className="max-w-5xl space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold">About</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Bump v{displayVersion} <span className="mx-2">·</span>{" "}
          <a
            href={repository.url}
            target="_blank"
            rel="noreferrer"
            className="hover:text-foreground inline-flex items-center gap-1 underline"
          >
            {repository.url.replace(/^https?:\/\//, "")}
            <ExternalLink className="h-3 w-3" />
          </a>
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Build</CardTitle>
        </CardHeader>
        <CardContent>
          <Row label="Version">{displayVersion}</Row>
          <Row label="Assembly">{build.assemblyVersion}</Row>
          {build.informationalVersion && (
            <Row label="Informational">{build.informationalVersion}</Row>
          )}
          {build.fileVersion && <Row label="File version">{build.fileVersion}</Row>}
          <Row label="Build date">{formatDate(build.buildDate)}</Row>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Server &amp; runtime</CardTitle>
        </CardHeader>
        <CardContent>
          <Row label="Host">{server.machineName}</Row>
          <Row label="Release environment">{server.releaseEnvironment}</Row>
          <Row label="Host environment">{server.environmentName}</Row>
          <Row label="Framework">{server.framework}</Row>
          <Row label="OS">{server.operatingSystem}</Row>
          <Row label="Architecture">{server.architecture}</Row>
          <Row label="CPU cores">{server.processorCount}</Row>
          <Row label="Working set">{formatBytes(server.workingSetBytes)}</Row>
          <Row label="GC heap">{formatBytes(server.gcTotalMemoryBytes)}</Row>
          <Row label="Process ID">{server.processId}</Row>
          <Row label="Started">{formatDate(server.processStartedAt)}</Row>
          <Row label="Uptime">{server.processUptime}</Row>
          <Row label="Server time">{formatDate(server.serverTime)}</Row>
          <Row label="Timezone">{server.timezone}</Row>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Database</CardTitle>
        </CardHeader>
        <CardContent>
          <Row label="Version">{database.version}</Row>
          <Row label="Host">{database.host ?? "—"}</Row>
          <Row label="Database">{database.databaseName ?? "—"}</Row>
          <Row label="Server time">{formatDate(database.serverTime)}</Row>
          <Row label="Migrations">{database.migrationCount}</Row>
          <Row label="Latest">
            {database.latestMigration ?? "—"}
            {database.latestMigrationAt && (
              <span className="text-muted-foreground">
                {" "}
                · {formatDate(database.latestMigrationAt)}
              </span>
            )}
          </Row>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Registry</CardTitle>
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-3 md:grid-cols-4">
          <Stat label="Apps" value={registry.apps} />
          <Stat
            label="Environments"
            value={
              registry.specialEnvironments > 0 ? `${registry.environments}` : registry.environments
            }
          />
          <Stat label="Tenants" value={registry.tenants} />
          <Stat
            label="Services"
            value={
              registry.servicesPaused > 0
                ? `${registry.services} (${registry.servicesPaused} paused)`
                : registry.services
            }
          />
          <Stat label="Open outages" value={registry.openOutages} />
          <Stat label="Outages (7d)" value={registry.outages7d} />
          <Stat label="Unresolved problems (24h)" value={registry.openProblemsDistinct24h} />
          <Stat label="Problem reports (24h)" value={registry.problemReports24h} />
          <Stat label="Problem reports (7d)" value={registry.problemReports7d} />
          <Stat label="Probe events (24h)" value={registry.probeEvents24h} />
          <Stat label="Active announcements" value={registry.activeAnnouncements} />
          <Stat label="Accounts" value={registry.accounts} />
        </CardContent>
      </Card>
    </div>
  );
}
