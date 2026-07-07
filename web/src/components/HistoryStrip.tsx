import type { ServiceStatus } from "@/lib/types";

const colors: Record<ServiceStatus, string> = {
  operational: "var(--color-success)",
  degraded: "var(--color-warning)",
  down: "var(--color-danger)",
};

const pausedColor = "var(--color-muted-foreground)";

export function HistoryStrip({ history, height = 32, paused = false }: { history: ServiceStatus[]; height?: number; paused?: boolean }) {
  const cellWidth = 4;
  const gap = 1;
  const width = history.length * (cellWidth + gap);
  return (
    <svg width="100%" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" style={{ height }}>
      {history.map((s, i) => (
        <rect
          key={i}
          x={i * (cellWidth + gap)}
          y={0}
          width={cellWidth}
          height={height}
          fill={paused ? pausedColor : (colors[s] ?? colors.operational)}
          rx={1}
        >
          <title>{paused ? "paused" : s}</title>
        </rect>
      ))}
    </svg>
  );
}
