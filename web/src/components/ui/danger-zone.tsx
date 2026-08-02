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
    <section className={cn("border-danger/40 bg-danger/5 rounded-card border", className)}>
      <header className="border-danger/30 text-danger border-b px-4 py-2 text-sm font-semibold">
        {title}
      </header>
      <div className="divide-danger/20 divide-y">{children}</div>
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
        {description && <div className="text-muted-foreground mt-0.5 text-xs">{description}</div>}
      </div>
      <div className="shrink-0">{action}</div>
    </div>
  );
}
