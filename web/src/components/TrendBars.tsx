interface TrendDay {
  date: string;
  label: string;
  outages: number;
  uptime: number;
  latencyMs: number;
  requests: number;
}

export function TrendBars({ data }: { data: TrendDay[] }) {
  const maxRequests = Math.max(1, ...data.map((d) => d.requests));
  return (
    <div className="flex h-28 items-stretch gap-2">
      {data.map((d) => {
        const empty = d.requests === 0;
        const h = empty ? 6 : Math.max(6, (d.requests / maxRequests) * 100);
        const color = empty
          ? "var(--color-border)"
          : d.uptime >= 99
            ? "var(--color-success)"
            : d.uptime >= 98
              ? "var(--color-warning)"
              : "var(--color-danger)";
        const title = empty
          ? `${d.label}: no data`
          : `${d.label}: uptime ${d.uptime}%, ${d.outages} outages, ${d.latencyMs}ms average`;
        return (
          <div key={d.date} className="flex flex-1 flex-col gap-1" title={title}>
            <div className="flex flex-1 items-end">
              <div
                style={{ width: "100%", height: `${h}%`, backgroundColor: color, borderRadius: 2 }}
              />
            </div>
            <span className="text-muted-foreground text-center text-xs">{d.label}</span>
            <span className="text-muted-foreground text-center text-xs tabular-nums">
              {empty ? "—" : d.requests.toLocaleString()}
            </span>
          </div>
        );
      })}
    </div>
  );
}
