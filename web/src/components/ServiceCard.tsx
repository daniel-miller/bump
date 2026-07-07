import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { StatusDot } from "@/components/StatusDot";
import { HistoryStrip } from "@/components/HistoryStrip";
import type { ServiceStatus } from "@/lib/types";

export interface ServiceCardData {
  name: string;
  url?: string;
  tenant?: string;
  environment?: string;
  paused: boolean;
  status: ServiceStatus;
  latencyMs: number;
  uptime: number;
  history: ServiceStatus[];
}

export function ServiceCard({ service, interactive = false }: { service: ServiceCardData; interactive?: boolean }) {
  const tag = service.tenant || service.environment
    ? `${service.tenant ?? ""}/${service.environment ?? ""}`
    : null;
  return (
    <Card className={interactive ? "hover:border-primary transition-colors" : undefined}>
      <CardContent className="p-4">
        <div className="flex items-center justify-between mb-2">
          <div className="flex items-center gap-2">
            <StatusDot status={service.status} paused={service.paused} />
            <div>
              <div className="font-medium flex items-center gap-2">
                {service.name}
                {service.paused && <Badge variant="default">Paused</Badge>}
              </div>
              <div className="text-xs text-muted-foreground">
                {service.url}
                {tag && <span className="ml-2">· {tag}</span>}
              </div>
            </div>
          </div>
          <div className="text-sm text-muted-foreground">
            {service.latencyMs} ms · {Number(service.uptime).toFixed(2)}%
          </div>
        </div>
        <HistoryStrip history={service.history} paused={service.paused} />
      </CardContent>
    </Card>
  );
}
