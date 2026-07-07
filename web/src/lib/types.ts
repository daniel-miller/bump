export type ServiceStatus = "operational" | "degraded" | "down";

export interface Kpi {
  label: string;
  value: string;
  subtitle: string;
  trend: "up" | "down";
}

export interface ServicePayload {
  slug: string;
  name: string;
  tenant: string;
  environment: string;
  url: string;
  paused: boolean;
  status: ServiceStatus;
  latencyMs: number;
  uptime: number;
  history: ServiceStatus[];
  lastOutageAt: string | null;
}

export interface OutagePayload {
  id: number;
  serviceSlug: string | null;
  title: string;
  status: "investigating" | "identified" | "monitoring" | "resolved";
  startedAt: string;
  resolvedAt: string | null;
  updates: { ts: string; msg: string; status: string }[];
}

export interface BoardSummary {
  slug: string;
  name: string;
}

export interface AnnouncementPayload {
  id: number;
  title: string;
  type: "info" | "warning" | "maintenance";
  content: string;
  publishAt: string;
  autoHideAt: string | null;
}

export interface StatusResponse {
  overall: ServiceStatus;
  updatedAt: string;
  board: BoardSummary | null;
  kpis: { operational: Kpi; uptime: Kpi; latency: Kpi; outages: Kpi; problems: Kpi };
  trend: { date: string; label: string; outages: number; uptime: number; latencyMs: number; requests: number }[];
  problemsTrend: { date: string; label: string; count: number }[];
  services: ServicePayload[];
  outages: OutagePayload[];
  boards: BoardSummary[];
  announcements: AnnouncementPayload[];
}

export interface MeResponse {
  userId: string;
  email: string;
  fullName: string;
  timezone: string;
  totpEnabled: boolean;
  ipAddress: string | null;
}
