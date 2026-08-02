import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { ServiceCard } from "@/components/ServiceCard";
import type { ServiceStatus } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";

interface ServiceRow {
  slug: string;
  name: string;
  url: string;
  tenant: string;
  environment: string;
  paused: boolean;
  isPrivate: boolean;
  lastStatus: ServiceStatus;
  latencyMs: number;
  uptime: number;
  history: ServiceStatus[];
  lastCheckAt: string | null;
}

// Allow lowercase a-z, 0-9, hyphens, underscores. Strips everything else.
function slugifyTag(input: string): string {
  return input.toLowerCase().replace(/[^a-z0-9_-]/g, "");
}

export function ServicesListPage() {
  const qc = useQueryClient();
  const { data = [], isLoading } = useQuery<ServiceRow[]>({
    queryKey: ["services"],
    queryFn: () => api<ServiceRow[]>("/api/admin/services"),
  });

  const [query, setQuery] = useState("");
  const [showPrivate, setShowPrivate] = useState(false);
  const [showNew, setShowNew] = useState(false);
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const [url, setUrl] = useState("");
  const [tenant, setTenant] = useState("");
  const [environment, setEnvironment] = useState("");
  const [error, setError] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: () =>
      api("/api/admin/services", {
        method: "POST",
        body: JSON.stringify({ slug, name, url, tenant, environment }),
      }),
    onSuccess: async () => {
      setShowNew(false);
      setSlug("");
      setName("");
      setUrl("");
      setTenant("");
      setEnvironment("");
      setError(null);
      await qc.invalidateQueries({ queryKey: ["services"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const q = query.trim().toLowerCase();
  const visible = showPrivate ? data : data.filter((m) => !m.isPrivate);
  const filtered =
    q.length === 0
      ? visible
      : visible.filter(
          (m) =>
            m.slug.toLowerCase().includes(q) ||
            m.name.toLowerCase().includes(q) ||
            m.url.toLowerCase().includes(q) ||
            m.tenant.toLowerCase().includes(q) ||
            m.environment.toLowerCase().includes(q),
        );

  return (
    <div className="space-y-4 p-6">
      <div className="flex items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Services</h1>
        <div className="flex items-center gap-2">
          <Input
            type="search"
            placeholder="Search slug, name, URL, tenant, environment"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="w-72"
            aria-label="Search services"
          />
          <Label
            htmlFor="show-private"
            className="flex cursor-pointer items-center gap-2 text-sm font-normal"
          >
            <Checkbox
              id="show-private"
              checked={showPrivate}
              onCheckedChange={(v) => setShowPrivate(v === true)}
            />
            Show private
          </Label>
          <Button onClick={() => setShowNew(true)}>New service</Button>
        </div>
      </div>

      {showNew && (
        <Card>
          <CardContent className="space-y-3 p-4">
            <div className="grid grid-cols-3 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="slug">Slug</Label>
                <Input
                  id="slug"
                  placeholder="my-service"
                  value={slug}
                  onChange={(e) => setSlug(e.target.value)}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="name">Name</Label>
                <Input
                  id="name"
                  placeholder="My Service"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="url">URL</Label>
                <Input
                  id="url"
                  placeholder="https://..."
                  value={url}
                  onChange={(e) => setUrl(e.target.value)}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="tenant">Tenant</Label>
                <Input
                  id="tenant"
                  placeholder="e.g. acme-corp"
                  value={tenant}
                  onChange={(e) => setTenant(slugifyTag(e.target.value))}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="environment">Environment</Label>
                <Input
                  id="environment"
                  placeholder="e.g. production"
                  value={environment}
                  onChange={(e) => setEnvironment(slugifyTag(e.target.value))}
                />
              </div>
            </div>
            {error && <div className="text-danger text-sm">{error}</div>}
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setShowNew(false)}>
                Cancel
              </Button>
              <Button onClick={() => create.mutate()} disabled={create.isPending}>
                Create service
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {isLoading && <div className="text-muted-foreground">Loading...</div>}

      <div className="grid grid-cols-1 gap-2 lg:grid-cols-2">
        {filtered.map((m) => (
          <Link to={`/admin/services/${m.slug}`} key={m.slug} className="block">
            <ServiceCard
              interactive
              service={{
                name: m.name,
                url: m.url,
                tenant: m.tenant,
                environment: m.environment,
                paused: m.paused,
                status: m.lastStatus,
                latencyMs: m.latencyMs,
                uptime: m.uptime,
                history: m.history,
              }}
            />
          </Link>
        ))}
        {!isLoading && data.length === 0 && (
          <div className="text-muted-foreground col-span-full text-sm">
            No services yet. Add one to start monitoring.
          </div>
        )}
        {!isLoading && data.length > 0 && filtered.length === 0 && (
          <div className="text-muted-foreground col-span-full text-sm">
            No services match "{query}".
          </div>
        )}
      </div>
    </div>
  );
}
