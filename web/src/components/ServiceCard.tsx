import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { StatusDot } from "@/components/StatusDot";
import { HistoryStrip } from "@/components/HistoryStrip";
import { environmentColor } from "@/lib/environmentColors";
import type { ServiceStatus } from "@/lib/types";

export interface ServiceCardData {
  name: string;
  url?: string;
  owner?: string;
  environment?: string;
  paused: boolean;
  status: ServiceStatus;
  latencyMs: number;
  uptime: number;
  history: ServiceStatus[];
}

export function ServiceCard({
  service,
  interactive = false,
}: {
  service: ServiceCardData;
  interactive?: boolean;
}) {
  const tag =
    service.owner || service.environment
      ? `${service.owner ?? ""}/${service.environment ?? ""}`
      : null;
  return (
    <Card className={interactive ? "hover:border-primary transition-colors" : undefined}>
      <CardContent className="p-4">
        <div className="mb-2 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <StatusDot status={service.status} paused={service.paused} />
            <div>
              <div className="flex items-center gap-2 font-medium">
                {service.name}
                {service.paused && <Badge variant="default">Paused</Badge>}
              </div>
              <div className="text-muted-foreground flex items-center gap-1.5 text-xs">
                <span>{service.url}</span>
                {tag && (
                  <>
                    <span>·</span>
                    {service.environment && (
                      <span
                        className="inline-block h-2.5 w-2.5 shrink-0 rounded-sm"
                        aria-hidden="true"
                        style={{ backgroundColor: environmentColor(service.environment) }}
                      />
                    )}
                    <span>{tag}</span>
                  </>
                )}
              </div>
            </div>
          </div>
          <div className="text-muted-foreground text-sm">
            {service.latencyMs} ms · {Number(service.uptime).toFixed(2)}%
          </div>
        </div>
        <HistoryStrip history={service.history} paused={service.paused} />
      </CardContent>
    </Card>
  );
}
