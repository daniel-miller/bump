import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";

interface ServerRecord {
  serverKey: number;
  serverNumber: number | null;
  serverHandle: string;
  serverName: string;
  serverDescription: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export function ServersPage() {
  const {
    data = [],
    isLoading,
    isError,
  } = useQuery<ServerRecord[]>({
    queryKey: ["admin", "servers"],
    queryFn: () => api<ServerRecord[]>("/api/admin/servers"),
  });

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Servers</h1>
        <p className="text-muted-foreground mt-1 text-sm">
          Server roster; names follow the NATO phonetic alphabet and the number is the leading
          digit of an IIS site ID
        </p>
      </div>
      {isLoading && <div className="text-muted-foreground text-sm">Loading...</div>}
      {isError && (
        <div className="text-danger text-sm">Couldn't load servers. Try refreshing the page.</div>
      )}
      {!isLoading && !isError && data.length === 0 && (
        <div className="text-muted-foreground text-sm">No servers registered yet.</div>
      )}
      {!isLoading && data.length > 0 && (
        <>
          <div className="border-border bg-card text-card-foreground divide-border max-w-2xl divide-y rounded border">
            {data.map((s) => (
              <div key={s.serverKey} className="flex items-center gap-3 px-3 py-2">
                <span className="text-muted-foreground w-7 shrink-0 text-right font-mono text-sm">
                  {s.serverNumber ?? ""}
                </span>
                <span className="text-muted-foreground w-5 shrink-0 font-mono text-sm">
                  {s.serverHandle}
                </span>
                <span className="flex-1 font-medium">{s.serverName}</span>
                {s.serverDescription && (
                  <span className="text-muted-foreground truncate text-xs">
                    {s.serverDescription}
                  </span>
                )}
              </div>
            ))}
          </div>
          <p className="text-muted-foreground max-w-2xl text-xs">
            Fleet servers take numbers from 1 (Alpha) upward; local workstations register as
            server 26 (Zulu) so their site IDs can never collide with a fleet server's
          </p>
        </>
      )}
    </div>
  );
}
