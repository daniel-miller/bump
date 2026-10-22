import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, api, problemMessage } from "@/lib/api";
import { parseAddresses } from "@/lib/addresses";
import { formatDate } from "@/lib/dates";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Textarea } from "@/components/ui/textarea";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DangerZone, DangerZoneItem } from "@/components/ui/danger-zone";
import { PageHeader } from "@/components/ui/page-header";

interface OwnerDetail {
  owner: {
    ownerId: number;
    ownerNumber: number | null;
    ownerHandle: string;
    ownerName: string;
    ownerDescription: string | null;
    ownerHost: string | null;
    ownerTheme: string | null;
  };
  serviceIds: number[];
}

// The API stores the theme as compact jsonb; re-indent for the editor.
function prettyTheme(json: string | null): string {
  if (!json) return "";
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}
interface ServiceRow {
  serviceId: number;
  handle: string;
  name: string;
  url: string;
}

export function OwnerDetailPage() {
  const { handle = "" } = useParams<{ handle: string }>();
  const qc = useQueryClient();
  const nav = useNavigate();
  const { data } = useQuery<OwnerDetail>({
    queryKey: ["owners", handle],
    queryFn: () => api<OwnerDetail>(`/api/admin/owners/${handle}`),
  });
  const { data: services = [] } = useQuery<ServiceRow[]>({
    queryKey: ["services"],
    queryFn: () => api<ServiceRow[]>("/api/admin/services"),
  });
  const [selected, setSelected] = useState<number[]>([]);
  const [initialSelected, setInitialSelected] = useState<number[]>([]);
  const [host, setHost] = useState("");
  const [initialHost, setInitialHost] = useState("");
  const [theme, setTheme] = useState("");
  const [initialTheme, setInitialTheme] = useState("");
  const [themeError, setThemeError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);

  useEffect(() => {
    if (data) {
      setSelected(data.serviceIds);
      setInitialSelected(data.serviceIds);
      setHost(data.owner.ownerHost ?? "");
      setInitialHost(data.owner.ownerHost ?? "");
      setTheme(prettyTheme(data.owner.ownerTheme));
      setInitialTheme(prettyTheme(data.owner.ownerTheme));
    }
  }, [data]);

  const dirty =
    selected.length !== initialSelected.length ||
    selected.some((id) => !initialSelected.includes(id));

  const save = useMutation({
    mutationFn: () =>
      api(`/api/admin/owners/${handle}`, {
        method: "PATCH",
        body: JSON.stringify({ serviceIds: selected }),
      }),
    onSuccess: async () => {
      setInitialSelected(selected);
      await qc.invalidateQueries({ queryKey: ["owners", handle] });
    },
  });

  const hostDirty = host.trim().toLowerCase() !== initialHost;
  const saveHost = useMutation({
    mutationFn: () =>
      api(`/api/admin/owners/${handle}`, {
        method: "PATCH",
        // Empty string clears the hostname; the API lowercases and validates.
        body: JSON.stringify({ host: host.trim() }),
      }),
    onSuccess: async () => {
      setInitialHost(host.trim().toLowerCase());
      await qc.invalidateQueries({ queryKey: ["owners", handle] });
    },
  });

  const themeDirty = theme.trim() !== initialTheme.trim();
  const saveTheme = useMutation({
    mutationFn: (body: unknown) =>
      api(`/api/admin/owners/${handle}/theme`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    onSuccess: async () => {
      setInitialTheme(theme.trim() === "" ? "" : prettyTheme(theme));
      setTheme((t) => (t.trim() === "" ? "" : prettyTheme(t)));
      await qc.invalidateQueries({ queryKey: ["owners", handle] });
    },
  });

  function submitTheme() {
    const text = theme.trim();
    if (text === "") {
      setThemeError(null);
      saveTheme.mutate(null);
      return;
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(text);
    } catch {
      setThemeError("Theme must be valid JSON.");
      return;
    }
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
      setThemeError("Theme must be a JSON object.");
      return;
    }
    setThemeError(null);
    saveTheme.mutate(parsed);
  }

  const remove = useMutation({
    mutationFn: () => api(`/api/admin/owners/${handle}`, { method: "DELETE" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["owners"] });
      nav("/owners");
    },
  });

  if (!data) return <div className="text-muted-foreground p-8">Loading...</div>;
  return (
    <div className="max-w-3xl space-y-4 p-6">
      <PageHeader
        title={
          <span className="flex items-baseline gap-2">
            {data.owner.ownerNumber !== null && (
              <span className="text-muted-foreground font-mono text-lg font-normal">
                {String(data.owner.ownerNumber).padStart(2, "0")}
              </span>
            )}
            {data.owner.ownerName}
          </span>
        }
        subtitle={
          <>
            {data.owner.ownerDescription && (
              <span className="block">{data.owner.ownerDescription}</span>
            )}
            <a
              href={`/boards/${data.owner.ownerHandle}`}
              target="_blank"
              rel="noreferrer"
              className="text-primary hover:underline"
            >
              /boards/{data.owner.ownerHandle}
            </a>
          </>
        }
      />
      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Services on this board</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {[...services].sort(byText((m) => m.name)).map((m) => {
            const checked = selected.includes(m.serviceId);
            return (
              <label key={m.serviceId} className="flex cursor-pointer items-center gap-2 text-sm">
                <Checkbox
                  checked={checked}
                  onCheckedChange={(v) => {
                    setSelected((s) =>
                      v ? [...s, m.serviceId] : s.filter((x) => x !== m.serviceId),
                    );
                  }}
                />
                <span>{m.name}</span>
                <span className="text-muted-foreground text-xs">{m.url}</span>
              </label>
            );
          })}
          {dirty && (
            <div className="flex justify-end gap-2 pt-3">
              <Button
                variant="outline"
                onClick={() => setSelected(initialSelected)}
                disabled={save.isPending}
              >
                Cancel
              </Button>
              <Button onClick={() => save.mutate()} disabled={save.isPending}>
                Save changes
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      <SubscribersCard handle={handle} ownerName={data.owner.ownerName} />

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Custom domain</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          <p className="text-muted-foreground text-sm">
            Serve this owner's status board at its own hostname instead of /boards/
            {data.owner.ownerHandle}. Point a CNAME at this server, then enter the bare hostname
            here. Leave empty to disable.
          </p>
          <Input
            value={host}
            onChange={(e) => setHost(e.target.value)}
            placeholder="status.example.com"
            className="font-mono"
          />
          {saveHost.isError && (
            <p className="text-danger text-sm">
              Couldn't save the hostname. Check the format and try again.
            </p>
          )}
          {hostDirty && (
            <div className="flex justify-end gap-2 pt-1">
              <Button
                variant="outline"
                onClick={() => setHost(initialHost)}
                disabled={saveHost.isPending}
              >
                Cancel
              </Button>
              <Button onClick={() => saveHost.mutate()} disabled={saveHost.isPending}>
                Save changes
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-sm">Board theme</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          <p className="text-muted-foreground text-sm">
            Optional design tokens applied to the public board when it is served from the custom
            hostname: light and dark color maps, card and badge radius, font, logo, and favicon.
            Leave empty for the default look. db/seed-openscorm-theme.sql in the repo is a complete
            example.
          </p>
          <Textarea
            value={theme}
            onChange={(e) => setTheme(e.target.value)}
            placeholder={
              '{\n  "font": "inter",\n  "light": { "primary": "#15803d" },\n  "dark": { "primary": "#6ee7a3" }\n}'
            }
            rows={14}
            className="font-mono text-xs"
            spellCheck={false}
          />
          {themeError && <p className="text-danger text-sm">{themeError}</p>}
          {saveTheme.isError && (
            <p className="text-danger text-sm">Couldn't save the theme. Try again.</p>
          )}
          {themeDirty && (
            <div className="flex justify-end gap-2 pt-1">
              <Button
                variant="outline"
                onClick={() => {
                  setTheme(initialTheme);
                  setThemeError(null);
                }}
                disabled={saveTheme.isPending}
              >
                Cancel
              </Button>
              <Button onClick={submitTheme} disabled={saveTheme.isPending}>
                Save changes
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      <DangerZone>
        <DangerZoneItem
          title="Delete owner"
          description="Permanently delete this owner. The public status page will stop working."
          action={
            <Button
              variant="destructive"
              onClick={() => setConfirmOpen(true)}
              disabled={remove.isPending}
            >
              Delete owner
            </Button>
          }
        />
      </DangerZone>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title="Delete owner?"
        description={`"${data.owner.ownerName}" will be removed permanently. This cannot be undone.`}
        confirmLabel="Delete owner"
        busy={remove.isPending}
        onConfirm={() => {
          setConfirmOpen(false);
          remove.mutate();
        }}
      />
    </div>
  );
}

interface SubscriberRow {
  subscriberId: number;
  subscriberEmail: string;
  confirmedAt: string | null;
  createdAt: string;
}

// Alphabetical, ignoring case and accents, so "acme" sits beside "Acme" rather than
// after every capitalized entry.
function byText<T>(key: (item: T) => string): (a: T, b: T) => number {
  return (a, b) => key(a).localeCompare(key(b), undefined, { sensitivity: "base" });
}

interface AddResult {
  added: string[];
  already: string[];
  invalid: string[];
  notAdded: string[];
  capped: boolean;
}

interface AddSummary {
  done: string | null;
  problems: string[];
}

function summarize(r: AddResult): AddSummary {
  const problems: string[] = [];
  if (r.already.length > 0) problems.push(`Already subscribed: ${r.already.join(", ")}.`);
  if (r.invalid.length > 0) problems.push(`Not valid addresses: ${r.invalid.join(", ")}.`);
  if (r.capped)
    problems.push(`The board reached its subscriber cap. Not added: ${r.notAdded.join(", ")}.`);
  else if (r.notAdded.length > 0)
    problems.push(`Could not add: ${r.notAdded.join(", ")}. Try again.`);
  const n = r.added.length;
  return { done: n > 0 ? `Added ${n} subscriber${n === 1 ? "" : "s"}.` : null, problems };
}

// The board's subscribers, which an operator can add, correct, and delete. Someone
// added here is subscribed at once; the API emails them that they were added, with a
// link to unsubscribe, in place of the confirmation link a self sign-up gets.
function SubscribersCard({ handle, ownerName }: { handle: string; ownerName: string }) {
  const qc = useQueryClient();
  const queryKey = ["owners", handle, "subscribers"];
  const {
    data: subscribers = [],
    isLoading,
    isError,
  } = useQuery<SubscriberRow[]>({
    queryKey,
    queryFn: () => api<SubscriberRow[]>(`/api/admin/owners/${handle}/subscribers`),
  });
  const [emails, setEmails] = useState("");
  const [summary, setSummary] = useState<AddSummary | null>(null);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editEmail, setEditEmail] = useState("");
  const [deleting, setDeleting] = useState<SubscriberRow | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = () => qc.invalidateQueries({ queryKey });

  const parsed = parseAddresses(emails);

  // One request per address, in order, so each gets the API's own answer: added,
  // already subscribed, or not a valid address. It stops at the subscriber cap,
  // since every later address would be refused the same way.
  const add = useMutation({
    mutationFn: async (addresses: string[]) => {
      const result: AddResult = {
        added: [],
        already: [],
        invalid: [],
        notAdded: [],
        capped: false,
      };
      for (const [i, address] of addresses.entries()) {
        try {
          await api(`/api/admin/owners/${handle}/subscribers`, {
            method: "POST",
            body: JSON.stringify({ email: address }),
          });
          result.added.push(address);
        } catch (err) {
          const title =
            err instanceof ApiError
              ? (err.problem as { title?: string } | undefined)?.title
              : undefined;
          if (title === "Already subscribed") result.already.push(address);
          else if (title === "Invalid email") result.invalid.push(address);
          else if (title === "Subscriber cap reached") {
            result.capped = true;
            result.notAdded.push(...addresses.slice(i));
            break;
          } else result.notAdded.push(address);
        }
      }
      return result;
    },
    onSuccess: async (result) => {
      // Leave only what still needs attention in the box, ready to correct.
      setEmails([...result.invalid, ...result.notAdded].join("\n"));
      setSummary(summarize(result));
      await refresh();
    },
  });

  const update = useMutation({
    mutationFn: (id: number) =>
      api(`/api/admin/owners/${handle}/subscribers/${id}`, {
        method: "PATCH",
        body: JSON.stringify({ email: editEmail.trim() }),
      }),
    onSuccess: async () => {
      setEditingId(null);
      setError(null);
      await refresh();
    },
    onError: (err: Error) => setError(problemMessage(err)),
  });

  const remove = useMutation({
    mutationFn: (id: number) =>
      api(`/api/admin/owners/${handle}/subscribers/${id}`, { method: "DELETE" }),
    onSuccess: async () => {
      setDeleting(null);
      setError(null);
      await refresh();
    },
    onError: (err: Error) => {
      setDeleting(null);
      setError(problemMessage(err));
    },
  });

  const confirmed = subscribers.filter((s) => s.confirmedAt).length;
  const pending = subscribers.length - confirmed;
  const sorted = [...subscribers].sort(byText((s) => s.subscriberEmail));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm">Subscribers</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-muted-foreground text-sm">
          Subscribers get an email when a service on this board has an outage and when it recovers,
          and announcements sent to subscribers. Anyone added here is subscribed at once, and
          receives an email saying so, with a link to unsubscribe.
        </p>
        <form
          className="space-y-2"
          onSubmit={(e) => {
            e.preventDefault();
            setError(null);
            setSummary(null);
            add.mutate(parsed);
          }}
        >
          <Textarea
            value={emails}
            onChange={(e) => setEmails(e.target.value)}
            placeholder="name@example.com, another@example.com"
            aria-label="Email addresses to subscribe"
            rows={2}
            spellCheck={false}
          />
          <div className="flex items-center justify-between gap-3">
            <p className="text-muted-foreground text-xs">
              One or more addresses, separated by commas, semicolons or new lines; the Outlook form
              Name &lt;address&gt; works too
            </p>
            <Button type="submit" disabled={parsed.length === 0 || add.isPending}>
              {add.isPending
                ? "Adding..."
                : parsed.length > 1
                  ? `Add ${parsed.length} subscribers`
                  : "Add subscriber"}
            </Button>
          </div>
        </form>
        {summary?.done && <p className="text-success text-sm">{summary.done}</p>}
        {summary?.problems.map((line) => (
          <p key={line} className="text-danger text-sm">
            {line}
          </p>
        ))}
        {error && <p className="text-danger text-sm">{error}</p>}
        {isLoading && <p className="text-muted-foreground text-sm">Loading...</p>}
        {isError && (
          <p className="text-danger text-sm">Couldn't load subscribers. Try refreshing the page.</p>
        )}
        {!isLoading && !isError && subscribers.length === 0 && (
          <p className="text-muted-foreground text-sm">No subscribers yet</p>
        )}
        {subscribers.length > 0 && (
          <>
            <p className="text-muted-foreground text-xs">
              {confirmed} confirmed
              {pending > 0 &&
                `, ${pending} pending: signed up on the board, waiting for them to click the confirmation link, and removed after 7 days`}
            </p>
            <ul className="divide-border divide-y">
              {sorted.map((s) => (
                <li key={s.subscriberId} className="flex items-center gap-3 py-2 text-sm">
                  {editingId === s.subscriberId ? (
                    <form
                      className="flex flex-1 gap-2"
                      onSubmit={(e) => {
                        e.preventDefault();
                        setError(null);
                        update.mutate(s.subscriberId);
                      }}
                    >
                      <Input
                        type="email"
                        value={editEmail}
                        onChange={(e) => setEditEmail(e.target.value)}
                        aria-label={`New address for ${s.subscriberEmail}`}
                        autoComplete="off"
                        autoFocus
                      />
                      <Button
                        type="submit"
                        size="sm"
                        disabled={!editEmail.trim() || update.isPending}
                      >
                        {update.isPending ? "Saving..." : "Save"}
                      </Button>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={() => setEditingId(null)}
                        disabled={update.isPending}
                      >
                        Cancel
                      </Button>
                    </form>
                  ) : (
                    <>
                      <span className="min-w-0 flex-1 truncate">{s.subscriberEmail}</span>
                      {s.confirmedAt ? (
                        <Badge variant="success">Confirmed</Badge>
                      ) : (
                        <Badge variant="secondary">Pending</Badge>
                      )}
                      <span className="text-muted-foreground text-xs whitespace-nowrap">
                        Added {formatDate(s.createdAt)}
                      </span>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => {
                          setEditingId(s.subscriberId);
                          setEditEmail(s.subscriberEmail);
                          setError(null);
                        }}
                      >
                        Edit
                      </Button>
                      <Button variant="outline" size="sm" onClick={() => setDeleting(s)}>
                        Delete
                      </Button>
                    </>
                  )}
                </li>
              ))}
            </ul>
          </>
        )}
      </CardContent>

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => {
          if (!open) setDeleting(null);
        }}
        title="Delete subscriber?"
        description={`${deleting?.subscriberEmail ?? ""} will stop receiving ${ownerName} status emails. This cannot be undone.`}
        confirmLabel="Delete subscriber"
        busy={remove.isPending}
        onConfirm={() => {
          if (deleting) remove.mutate(deleting.subscriberId);
        }}
      />
    </Card>
  );
}
