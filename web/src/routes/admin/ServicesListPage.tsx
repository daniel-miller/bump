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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

interface ServiceRow {
  handle: string;
  name: string;
  url: string;
  owner: string;
  environment: string;
  paused: boolean;
  isPrivate: boolean;
  lastStatus: ServiceStatus;
  latencyMs: number;
  uptime: number;
  history: ServiceStatus[];
  lastCheckAt: string | null;
}

interface OwnerOption {
  ownerHandle: string;
  ownerName: string;
}

interface EnvironmentOption {
  environmentHandle: string;
  environmentName: string;
}

export function ServicesListPage() {
  const qc = useQueryClient();
  const {
    data = [],
    isLoading,
    isError,
  } = useQuery<ServiceRow[]>({
    queryKey: ["services"],
    queryFn: () => api<ServiceRow[]>("/api/admin/services"),
  });

  const [query, setQuery] = useState("");
  const [showPrivate, setShowPrivate] = useState(false);
  const [showNew, setShowNew] = useState(false);
  const [handle, setHandle] = useState("");
  const [name, setName] = useState("");
  const [url, setUrl] = useState("");
  const [owner, setOwner] = useState("");
  const [environment, setEnvironment] = useState("");
  const [error, setError] = useState<string | null>(null);

  // Canon tokens only: owner and environment come from the rosters, so an
  // alias or a typo can never be written. Aliases are for reading.
  const { data: owners = [] } = useQuery<OwnerOption[]>({
    queryKey: ["owners"],
    queryFn: () => api<OwnerOption[]>("/api/admin/owners"),
    enabled: showNew,
  });
  const { data: environments = [] } = useQuery<EnvironmentOption[]>({
    queryKey: ["admin", "environments"],
    queryFn: () => api<EnvironmentOption[]>("/api/admin/environments"),
    enabled: showNew,
  });

  const create = useMutation({
    mutationFn: () =>
      api("/api/admin/services", {
        method: "POST",
        body: JSON.stringify({ handle, name, url, owner, environment }),
      }),
    onSuccess: async () => {
      setShowNew(false);
      setHandle("");
      setName("");
      setUrl("");
      setOwner("");
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
            m.handle.toLowerCase().includes(q) ||
            m.name.toLowerCase().includes(q) ||
            m.url.toLowerCase().includes(q) ||
            m.owner.toLowerCase().includes(q) ||
            m.environment.toLowerCase().includes(q),
        );

  return (
    <div className="space-y-4 p-6">
      <div className="flex items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Services</h1>
        <div className="flex items-center gap-2">
          <Input
            type="search"
            placeholder="Search handle, name, URL, owner, environment"
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
                <Label htmlFor="handle">Handle</Label>
                <Input
                  id="handle"
                  placeholder="my-service"
                  value={handle}
                  onChange={(e) => setHandle(e.target.value)}
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
                <Label htmlFor="owner">Owner</Label>
                <Select value={owner} onValueChange={setOwner}>
                  <SelectTrigger id="owner">
                    <SelectValue placeholder="Select owner" />
                  </SelectTrigger>
                  <SelectContent>
                    {owners.map((o) => (
                      <SelectItem key={o.ownerHandle} value={o.ownerHandle}>
                        <span className="font-mono">{o.ownerHandle}</span>
                        <span className="text-muted-foreground ml-2 text-xs">{o.ownerName}</span>
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="environment">Environment</Label>
                <Select value={environment} onValueChange={setEnvironment}>
                  <SelectTrigger id="environment">
                    <SelectValue placeholder="Select environment" />
                  </SelectTrigger>
                  <SelectContent>
                    {environments.map((e) => (
                      <SelectItem key={e.environmentHandle} value={e.environmentHandle}>
                        <span className="font-mono">{e.environmentHandle}</span>
                        <span className="text-muted-foreground ml-2 text-xs">
                          {e.environmentName}
                        </span>
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            {error && <div className="text-danger text-sm">{error}</div>}
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setShowNew(false)}>
                Cancel
              </Button>
              <Button
                onClick={() => create.mutate()}
                disabled={!handle || !name || !url || !owner || !environment || create.isPending}
              >
                Create service
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {isLoading && <div className="text-muted-foreground">Loading...</div>}
      {/* A failed fetch must never read as an empty roster. */}
      {isError && (
        <div className="text-danger text-sm">Couldn't load services. Try refreshing the page.</div>
      )}

      <div className="grid grid-cols-1 gap-2 lg:grid-cols-2">
        {filtered.map((m) => (
          <Link to={`/services/${m.handle}`} key={m.handle} className="block">
            <ServiceCard
              interactive
              service={{
                name: m.name,
                url: m.url,
                handle: m.handle,
                owner: m.owner,
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
        {!isLoading && !isError && data.length === 0 && (
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
