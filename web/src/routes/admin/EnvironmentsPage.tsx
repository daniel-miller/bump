import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { environmentColor } from "@/lib/environmentColors";

interface EnvironmentRecord {
  environmentKey: number;
  environmentNumber: number | null;
  environmentHandle: string;
  environmentName: string;
  environmentDescription: string | null;
  environmentAliases: string[];
  isSpecialPurpose: boolean;
  isDerivedFromLive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

function EnvCard({ env }: { env: EnvironmentRecord }) {
  return (
    <div className="border-border bg-card text-card-foreground rounded border">
      <div className="flex items-start gap-3 p-3">
        <div
          className="mt-1 h-4 w-4 shrink-0 rounded-sm"
          aria-hidden="true"
          style={{ backgroundColor: environmentColor(env.environmentHandle, env.environmentAliases) }}
        />
        <div className="min-w-0 flex-1">
          <div className="flex items-baseline gap-2">
            <span className="font-medium">{env.environmentName}</span>
            {env.environmentNumber !== null && (
              <span className="text-muted-foreground font-mono text-xs">
                {env.environmentNumber}
              </span>
            )}
          </div>
          <div className="text-muted-foreground mt-1 text-sm">{env.environmentDescription}</div>
          {env.environmentAliases.length > 0 && (
            <div className="mt-3 flex flex-wrap gap-1">
              {env.environmentAliases.map((a) => (
                <span
                  key={a}
                  className="bg-muted text-muted-foreground rounded px-2 py-0.5 font-mono text-xs"
                >
                  {a}
                </span>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
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

  // Work is listed everywhere, not just on a localhost origin: this page is
  // the full roster documentation, and hiding a lifecycle stage made the
  // roster look shorter than it is.
  const lifecycle = data.filter((e) => !e.isSpecialPurpose);
  const derived = data.filter((e) => e.isSpecialPurpose && e.isDerivedFromLive);
  const independent = data.filter((e) => e.isSpecialPurpose && !e.isDerivedFromLive);

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Environments</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Deployment environments and their accepted aliases; numbers and order follow the infra
          roster
        </p>
      </div>
      {isLoading && <div className="text-muted-foreground text-sm">Loading...</div>}
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
          <section className="space-y-2">
            <h2 className="text-muted-foreground text-sm font-medium">Lifecycle</h2>
            {lifecycle.map((env) => (
              <EnvCard key={env.environmentKey} env={env} />
            ))}
          </section>

          <div className="space-y-4">
            <section className="space-y-2">
              <h2 className="text-muted-foreground text-sm font-medium">Derived from live</h2>
              {derived.map((env) => (
                <EnvCard key={env.environmentKey} env={env} />
              ))}
            </section>

            <section className="space-y-2">
              <h2 className="text-muted-foreground text-sm font-medium">Independent</h2>
              {independent.map((env) => (
                <EnvCard key={env.environmentKey} env={env} />
              ))}
            </section>
          </div>
        </div>
      )}
    </div>
  );
}
