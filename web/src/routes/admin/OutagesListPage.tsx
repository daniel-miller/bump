import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { api } from "@/lib/api";
import { formatAbsolute } from "@/lib/dates";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";

interface OutageRow {
  outageId: number;
  monitorId: number | null;
  outageTitle: string;
  outageStatus: string;
  startedAt: string;
  resolvedAt: string | null;
}

export function OutagesListPage() {
  const { data = [], isLoading } = useQuery<OutageRow[]>({
    queryKey: ["outages", "all"],
    queryFn: () => api<OutageRow[]>("/api/admin/outages?status=all"),
  });
  return (
    <div className="space-y-4 p-6">
      <h1 className="text-2xl font-semibold">Outages</h1>
      {isLoading && <div className="text-muted-foreground">Loading...</div>}
      <div className="space-y-2">
        {data.map((i) => (
          <Link to={`/admin/outages/${i.outageId}`} key={i.outageId} className="block">
            <Card className="hover:border-primary transition-colors">
              <CardContent className="flex items-center justify-between p-4">
                <div>
                  <div className="font-medium">{i.outageTitle}</div>
                  <div className="text-muted-foreground text-xs">
                    Started {formatAbsolute(i.startedAt)}
                    {i.resolvedAt && ` · Resolved ${formatAbsolute(i.resolvedAt)}`}
                  </div>
                </div>
                <Badge className="capitalize">{i.outageStatus}</Badge>
              </CardContent>
            </Card>
          </Link>
        ))}
        {!isLoading && data.length === 0 && (
          <div className="text-muted-foreground text-sm">
            No outages reported. All systems are calm.
          </div>
        )}
      </div>
    </div>
  );
}
