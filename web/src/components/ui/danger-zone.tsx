import * as React from "react";
import { cn } from "@/lib/cn";

export function DangerZone({
  className,
  title = "Danger zone",
  children,
}: {
  className?: string;
  title?: string;
  children: React.ReactNode;
}) {
  return (
    <section className={cn("rounded border border-danger/40 bg-danger/5", className)}>
      <header className="px-4 py-2 border-b border-danger/30 text-sm font-semibold text-danger">
        {title}
      </header>
      <div className="divide-y divide-danger/20">{children}</div>
    </section>
  );
}

export function DangerZoneItem({
  title,
  description,
  action,
}: {
  title: string;
  description?: React.ReactNode;
  action: React.ReactNode;
}) {
  return (
    <div className="flex items-center justify-between gap-4 px-4 py-3">
      <div className="min-w-0">
        <div className="text-sm font-medium">{title}</div>
        {description && (
          <div className="text-xs text-muted-foreground mt-0.5">{description}</div>
        )}
      </div>
      <div className="shrink-0">{action}</div>
    </div>
  );
}
