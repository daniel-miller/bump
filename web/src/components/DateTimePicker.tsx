import * as React from "react";
import { format } from "date-fns";
import { Calendar as CalendarIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Calendar } from "@/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { cn } from "@/lib/cn";

interface DateTimePickerProps {
  value: Date | null;
  onChange: (value: Date | null) => void;
  placeholder?: string;
  className?: string;
}

export function DateTimePicker({
  value,
  onChange,
  placeholder = "Pick a date and time",
  className,
}: DateTimePickerProps) {
  const timeValue = value ? format(value, "HH:mm") : "";

  function handleDateSelect(d: Date | undefined) {
    if (!d) {
      onChange(null);
      return;
    }
    const next = new Date(d);
    if (value) {
      next.setHours(value.getHours(), value.getMinutes(), 0, 0);
    } else {
      next.setHours(0, 0, 0, 0);
    }
    onChange(next);
  }

  function handleTimeChange(e: React.ChangeEvent<HTMLInputElement>) {
    const [hh, mm] = e.target.value.split(":").map(Number);
    if (Number.isNaN(hh) || Number.isNaN(mm)) return;
    const base = value ?? new Date();
    const next = new Date(base);
    next.setHours(hh, mm, 0, 0);
    onChange(next);
  }

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          className={cn(
            "justify-start text-left font-normal",
            !value && "text-muted-foreground",
            className
          )}
        >
          <CalendarIcon className="h-4 w-4" />
          {value ? format(value, "PPP p") : <span>{placeholder}</span>}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0">
        <Calendar mode="single" selected={value ?? undefined} onSelect={handleDateSelect} />
        <div className="border-t border-border p-3">
          <label className="text-xs text-muted-foreground block mb-1">Time</label>
          <input
            type="time"
            value={timeValue}
            onChange={handleTimeChange}
            className="px-2 py-1 bg-muted border border-border rounded text-sm"
          />
        </div>
      </PopoverContent>
    </Popover>
  );
}
