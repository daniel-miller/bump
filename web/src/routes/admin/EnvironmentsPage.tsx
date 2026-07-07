import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { environmentColor } from "@/lib/environmentColors";
import { Card, CardContent } from "@/components/ui/card";

interface EnvironmentRecord {
  environmentKey: number;
  environmentSlug: string;
  environmentName: string;
  environmentDescription: string | null;
  environmentAliases: string[];
  isSpecialPurpose: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export function EnvironmentsPage() {
  const { data = [], isLoading, isError } = useQuery<EnvironmentRecord[]>({
    queryKey: ["admin", "environments"],
    queryFn: () => api<EnvironmentRecord[]>("/api/admin/environments"),
  });

  const lifecycle = data.filter((e) => !e.isSpecialPurpose);
  const special = data.filter((e) => e.isSpecialPurpose);

  return (
    <div className="p-6 space-y-4">
      <h1 className="text-2xl font-semibold">Environments</h1>
      {isLoading && <div className="text-sm text-muted-foreground">Loading…</div>}
      {isError && <div className="text-sm text-danger">Couldn't load environments. Try refreshing the page.</div>}
      {!isLoading && !isError && data.length === 0 && (
        <div className="text-sm text-muted-foreground">No environments registered yet.</div>
      )}
      {!isLoading && data.length > 0 && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <EnvironmentColumn title="Lifecycle" emptyLabel="No lifecycle environments." items={lifecycle} />
          <EnvironmentColumn title="Special Purpose" emptyLabel="No special-purpose environments." items={special} />
        </div>
      )}
    </div>
  );
}

function EnvironmentColumn({
  title,
  items,
  emptyLabel,
}: {
  title: string;
  items: EnvironmentRecord[];
  emptyLabel: string;
}) {
  return (
    <section className="space-y-2">
      <h2 className="text-sm font-medium text-muted-foreground">{title}</h2>
      {items.length === 0 ? (
        <div className="text-sm text-muted-foreground">{emptyLabel}</div>
      ) : (
        items.map((e) => (
          <Card key={e.environmentKey} className={e.isSpecialPurpose ? "bg-muted/40" : undefined}>
            <CardContent className="p-3 flex items-start gap-3">
              <div
                className="h-4 w-4 rounded-sm shrink-0 mt-1"
                style={{ backgroundColor: environmentColor(e.environmentSlug, e.environmentAliases) }}
                aria-hidden
              />
              <div className="min-w-0 flex-1">
                <div className="font-medium">{e.environmentName}</div>
                <div className="text-xs text-muted-foreground">{e.environmentSlug}</div>
                {e.environmentDescription && (
                  <div className="mt-1 text-sm text-muted-foreground">{e.environmentDescription}</div>
                )}
              </div>
              {e.environmentAliases.length > 0 && (
                <div className="flex flex-wrap gap-1 justify-end max-w-[50%]">
                  {e.environmentAliases.map((a) => (
                    <span
                      key={a}
                      className="px-2 py-0.5 rounded text-xs font-mono bg-muted text-muted-foreground"
                    >
                      {a}
                    </span>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        ))
      )}
    </section>
  );
}
