import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { environmentSwatchLabel, environmentSwatchStyle } from "@/lib/environmentColors";

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
          // The record's own flag wins over the handle heuristic: this row came
          // from the database, which is where the answer lives.
          style={environmentSwatchStyle(
            env.environmentHandle,
            env.environmentAliases,
            env.isDerivedFromLive,
          )}
          title={environmentSwatchLabel(
            env.environmentHandle,
            env.environmentAliases,
            env.isDerivedFromLive,
          )}
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
          {env.isDerivedFromLive && (
            <div className="text-muted-foreground mt-2 text-xs">Holds real production data</div>
          )}
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

/** Same swatch the cards use, for the legend, where there is no record to read. */
function LegendSwatch({ handle, derivedFromLive }: { handle: string; derivedFromLive?: boolean }) {
  return (
    <span
      className="inline-block h-3 w-3 shrink-0 rounded-sm"
      aria-hidden="true"
      style={environmentSwatchStyle(handle, [], derivedFromLive)}
    />
  );
}

/**
 * The palette carries two independent facts, and a reader who does not know
 * that reads the ring as decoration. Kept in step with the same legend on
 * cmds-app's environments page; canon is daniel-miller/infra/README.md.
 */
function Legend() {
  return (
    <div className="border-border bg-card text-card-foreground rounded border p-4">
      <div className="text-sm font-medium">Reading a swatch</div>
      <div className="text-muted-foreground mt-3 grid gap-3 text-sm sm:grid-cols-2">
        <div>
          <div className="flex items-center gap-2">
            {["work", "test", "stage", "live"].map((h) => (
              <LegendSwatch key={h} handle={h} />
            ))}
          </div>
          <div className="mt-2">
            <span className="text-foreground">Color</span> is promotion distance: how many gates
            stand between a change made here and production. Work has three, test two, stage one,
            live none. Gray means the environment is off that path entirely. It is not a traffic
            light and says nothing about health.
          </div>
        </div>
        <div>
          <div className="flex items-center gap-2">
            <LegendSwatch handle="echo" derivedFromLive />
            <span className="inline-block w-1" />
            <LegendSwatch handle="demo" />
            <LegendSwatch handle="__unmapped__" />
          </div>
          <div className="mt-2">
            <span className="text-foreground">A ring</span> in live&rsquo;s color means the data is
            copied from production. No ring means it is not, so Demo&rsquo;s invented data never
            looks like customer records. <span className="text-foreground">Dashed and hollow</span>{" "}
            is a handle that is not in the roster at all.
          </div>
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
      {!isLoading && data.length > 0 && <Legend />}
      {!isLoading && data.length > 0 && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <section className="space-y-2">
            <h2 className="text-muted-foreground text-sm font-medium">Lifecycle</h2>
            <p className="text-muted-foreground text-sm">
              Listed in the order work moves through them. Ordered by proximity to production in the
              infra roster, which numbers live 1 and work 4.
            </p>
            {lifecycle.map((env) => (
              <EnvCard key={env.environmentKey} env={env} />
            ))}
          </section>

          <div className="space-y-4">
            <section className="space-y-2">
              <h2 className="text-muted-foreground text-sm font-medium">Derived from live</h2>
              <p className="text-muted-foreground text-sm">
                No work passes through these; their data comes from production, so they inherit its
                handling rules. The difference between them is temperature. Echo is hot and tracks
                live within minutes; Cold is an icebox nobody expects to be current.
              </p>
              {derived.map((env) => (
                <EnvCard key={env.environmentKey} env={env} />
              ))}
            </section>

            <section className="space-y-2">
              <h2 className="text-muted-foreground text-sm font-medium">Independent</h2>
              <p className="text-muted-foreground text-sm">
                Neither a stage on the way to production nor a copy of it. Descended from nothing,
                so it is the one environment where &ldquo;is this current?&rdquo; does not matter.
              </p>
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
