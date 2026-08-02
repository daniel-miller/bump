import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatAbsolute } from "@/lib/dates";
import { DateTimePicker } from "@/components/DateTimePicker";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";

interface Row {
  announcementId: number;
  boardId: number | null;
  announcementTitle: string;
  announcementType: string;
  announcementContent: string;
  publishAt: string;
  autoHideAt: string | null;
}

export function AnnouncementsPage() {
  const qc = useQueryClient();
  const { data = [], isLoading } = useQuery<Row[]>({
    queryKey: ["announcements"],
    queryFn: () => api<Row[]>("/api/admin/announcements"),
  });
  const [title, setTitle] = useState("");
  const [type, setType] = useState("info");
  const [content, setContent] = useState("");
  const [publishAt, setPublishAt] = useState<Date | null>(null);
  const [autoHideAt, setAutoHideAt] = useState<Date | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmId, setConfirmId] = useState<number | null>(null);

  const create = useMutation({
    mutationFn: () =>
      api("/api/admin/announcements", {
        method: "POST",
        body: JSON.stringify({
          title,
          type,
          content,
          publishAt: (publishAt ?? new Date()).toISOString(),
          autoHideAt: autoHideAt ? autoHideAt.toISOString() : null,
          notifySubscribers: true,
        }),
      }),
    onSuccess: async () => {
      setTitle("");
      setContent("");
      setPublishAt(null);
      setAutoHideAt(null);
      setError(null);
      await qc.invalidateQueries({ queryKey: ["announcements"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const remove = useMutation({
    mutationFn: (id: number) => api(`/api/admin/announcements/${id}`, { method: "DELETE" }),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["announcements"] });
    },
  });

  function onCreate() {
    if (autoHideAt && publishAt && autoHideAt <= publishAt) {
      setError("Auto-hide time must be after publish time.");
      return;
    }
    setError(null);
    create.mutate();
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="text-2xl font-semibold">Announcements</h1>
      <Card className="max-w-2xl">
        <CardContent className="space-y-3 p-4">
          <div className="space-y-1.5">
            <Label htmlFor="title">Title</Label>
            <Input
              id="title"
              placeholder="Scheduled maintenance — May 15"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
          </div>
          <div className="flex gap-2">
            <div className="w-44 space-y-1.5">
              <Label>Type</Label>
              <Select value={type} onValueChange={setType}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="info">Info</SelectItem>
                  <SelectItem value="warning">Warning</SelectItem>
                  <SelectItem value="maintenance">Maintenance</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="flex-1 space-y-1.5">
              <Label>Publish at</Label>
              <DateTimePicker
                value={publishAt}
                onChange={setPublishAt}
                placeholder="Pick date and time"
                className="w-full"
              />
            </div>
          </div>
          <div className="space-y-1.5">
            <div className="flex items-center justify-between">
              <Label>Auto-hide at</Label>
              {autoHideAt && (
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-6 px-2 text-xs"
                  onClick={() => setAutoHideAt(null)}
                >
                  Clear
                </Button>
              )}
            </div>
            <DateTimePicker
              value={autoHideAt}
              onChange={setAutoHideAt}
              placeholder="Optional — never hides if blank"
              className="w-full"
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="content">Content</Label>
            <Textarea
              id="content"
              rows={4}
              placeholder="Describe the announcement. Subscribers see this in email."
              value={content}
              onChange={(e) => setContent(e.target.value)}
            />
          </div>
          {error && <div className="text-danger text-sm">{error}</div>}
          <div className="flex justify-end">
            <Button onClick={onCreate} disabled={!title || !content || create.isPending}>
              Create announcement
            </Button>
          </div>
        </CardContent>
      </Card>
      <div className="space-y-2">
        {data.map((a) => (
          <Card key={a.announcementId}>
            <CardContent className="flex items-start justify-between gap-3 p-3">
              <div className="min-w-0 flex-1">
                <div className="font-medium">{a.announcementTitle}</div>
                <div className="text-muted-foreground text-xs">
                  {a.announcementType} · publishes {formatAbsolute(a.publishAt)}
                  {a.autoHideAt && ` · hides ${formatAbsolute(a.autoHideAt)}`}
                </div>
                <div className="mt-1 whitespace-pre-wrap">{a.announcementContent}</div>
              </div>
              <Button
                variant="destructive"
                size="sm"
                disabled={remove.isPending && remove.variables === a.announcementId}
                onClick={() => setConfirmId(a.announcementId)}
              >
                Delete
              </Button>
            </CardContent>
          </Card>
        ))}
        {!isLoading && data.length === 0 && (
          <div className="text-muted-foreground text-sm">
            No announcements yet. Post one to update subscribers.
          </div>
        )}
      </div>

      <ConfirmDialog
        open={confirmId !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmId(null);
        }}
        title="Delete announcement?"
        description={
          confirmId !== null
            ? `"${data.find((a) => a.announcementId === confirmId)?.announcementTitle ?? ""}" will be removed permanently.`
            : ""
        }
        confirmLabel="Delete announcement"
        variant="danger"
        disabled={remove.isPending}
        onConfirm={() => {
          if (confirmId !== null) {
            remove.mutate(confirmId);
            setConfirmId(null);
          }
        }}
      />
    </div>
  );
}
