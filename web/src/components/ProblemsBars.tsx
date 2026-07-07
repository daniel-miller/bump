interface ProblemDay {
  date: string;
  label: string;
  count: number;
}

export function ProblemsBars({ data }: { data: ProblemDay[] }) {
  const maxCount = Math.max(1, ...data.map((d) => d.count));
  return (
    <div className="flex items-stretch gap-2 h-28">
      {data.map((d) => {
        const empty = d.count === 0;
        const h = empty ? 6 : Math.max(6, (d.count / maxCount) * 100);
        const color = empty ? "var(--color-border)" : "var(--color-danger)";
        const title = empty ? `${d.label}: no problems` : `${d.label}: ${d.count} problem${d.count === 1 ? "" : "s"}`;
        return (
          <div key={d.date} className="flex-1 flex flex-col gap-1" title={title}>
            <div className="flex-1 flex items-end">
              <div style={{ width: "100%", height: `${h}%`, backgroundColor: color, borderRadius: 2 }} />
            </div>
            <span className="text-xs text-muted-foreground text-center">{d.label}</span>
            <span className="text-xs text-muted-foreground text-center tabular-nums">{empty ? "—" : d.count.toLocaleString()}</span>
          </div>
        );
      })}
    </div>
  );
}
