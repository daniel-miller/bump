interface ProblemDay {
  date: string;
  label: string;
  count: number;
}

export function ProblemsBars({ data }: { data: ProblemDay[] }) {
  const maxCount = Math.max(1, ...data.map((d) => d.count));
  return (
    <div className="flex h-28 items-stretch gap-2">
      {data.map((d) => {
        const empty = d.count === 0;
        const h = empty ? 6 : Math.max(6, (d.count / maxCount) * 100);
        const color = empty ? "var(--color-border)" : "var(--color-danger)";
        const title = empty
          ? `${d.label}: no problems`
          : `${d.label}: ${d.count} problem${d.count === 1 ? "" : "s"}`;
        return (
          <div key={d.date} className="flex flex-1 flex-col gap-1" title={title}>
            <div className="flex flex-1 items-end">
              <div
                style={{ width: "100%", height: `${h}%`, backgroundColor: color, borderRadius: 2 }}
              />
            </div>
            <span className="text-muted-foreground text-center text-xs">{d.label}</span>
            <span className="text-muted-foreground text-center text-xs tabular-nums">
              {empty ? "—" : d.count.toLocaleString()}
            </span>
          </div>
        );
      })}
    </div>
  );
}
