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
  const {
    data = [],
    isLoading,
    isError,
  } = useQuery<EnvironmentRecord[]>({
    queryKey: ["admin", "environments"],
    queryFn: () => api<EnvironmentRecord[]>("/api/admin/environments"),
  });

  const lifecycle = data.filter((e) => !e.isSpecialPurpose);
  const special = data.filter((e) => e.isSpecialPurpose);

  return (
    <div className="space-y-4 p-6">
      <h1 className="text-2xl font-semibold">Environments</h1>
      {isLoading && <div className="text-muted-foreground text-sm">Loading…</div>}
      {isError && (
        <div className="text-danger text-sm">
          Couldn't load environments. Try refreshing the page.
        </div>
      )}
      {!isLoading && !isError && data.length === 0 && (
        <div className="text-muted-foreground text-sm">No environments registered yet.</div>
      )}
      {!isLoading && data.length > 0 && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <EnvironmentColumn
            title="Lifecycle"
            emptyLabel="No lifecycle environments."
            items={lifecycle}
          />
          <EnvironmentColumn
            title="Special Purpose"
            emptyLabel="No special-purpose environments."
            items={special}
          />
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
      <h2 className="text-muted-foreground text-sm font-medium">{title}</h2>
      {items.length === 0 ? (
        <div className="text-muted-foreground text-sm">{emptyLabel}</div>
      ) : (
        items.map((e) => (
          <Card key={e.environmentKey} className={e.isSpecialPurpose ? "bg-muted/40" : undefined}>
            <CardContent className="flex items-start gap-3 p-3">
              <div
                className="mt-1 h-4 w-4 shrink-0 rounded-sm"
                style={{
                  backgroundColor: environmentColor(e.environmentSlug, e.environmentAliases),
                }}
                aria-hidden
              />
              <div className="min-w-0 flex-1">
                <div className="font-medium">{e.environmentName}</div>
                <div className="text-muted-foreground text-xs">{e.environmentSlug}</div>
                {e.environmentDescription && (
                  <div className="text-muted-foreground mt-1 text-sm">
                    {e.environmentDescription}
                  </div>
                )}
              </div>
              {e.environmentAliases.length > 0 && (
                <div className="flex max-w-[50%] flex-wrap justify-end gap-1">
                  {e.environmentAliases.map((a) => (
                    <span
                      key={a}
                      className="bg-muted text-muted-foreground rounded px-2 py-0.5 font-mono text-xs"
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
