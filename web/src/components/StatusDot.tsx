import { Pause } from "lucide-react";
import type { ServiceStatus } from "@/lib/types";

const colors: Record<ServiceStatus, string> = {
  operational: "var(--color-success)",
  degraded: "var(--color-warning)",
  down: "var(--color-danger)",
};

const STATUS_ARIA_LABELS: Record<ServiceStatus, string> = {
  operational: "Operational",
  degraded: "Degraded",
  down: "Down",
};

export function StatusDot({
  status,
  size = 10,
  paused = false,
}: {
  status: ServiceStatus;
  size?: number;
  paused?: boolean;
}) {
  if (paused) {
    return (
      <Pause
        aria-label="Paused"
        size={size + 4}
        style={{ color: "var(--color-muted-foreground)" }}
        fill="currentColor"
        strokeWidth={0}
      />
    );
  }
  return (
    <span
      aria-label={STATUS_ARIA_LABELS[status] ?? status}
      style={{
        display: "inline-block",
        width: size,
        height: size,
        borderRadius: "50%",
        backgroundColor: colors[status],
      }}
    />
  );
}
