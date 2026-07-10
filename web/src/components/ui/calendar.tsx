import { DayPicker, type DayPickerProps } from "react-day-picker";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { cn } from "@/lib/cn";
import "react-day-picker/style.css";

export type CalendarProps = DayPickerProps;

export function Calendar({ className, classNames, ...props }: CalendarProps) {
  return (
    <DayPicker
      showOutsideDays
      className={cn("p-2", className)}
      classNames={{
        months: "flex flex-col space-y-2",
        month: "space-y-2",
        month_caption: "flex justify-center pt-1 relative items-center",
        caption_label: "text-sm font-medium",
        nav: "flex items-center justify-between absolute inset-x-1 top-1",
        button_previous: "h-7 w-7 inline-flex items-center justify-center rounded hover:bg-muted",
        button_next: "h-7 w-7 inline-flex items-center justify-center rounded hover:bg-muted",
        month_grid: "w-full border-collapse",
        weekdays: "flex",
        weekday: "text-muted-foreground w-8 text-xs font-normal text-center",
        week: "flex w-full mt-1",
        day: "h-8 w-8 text-center text-sm p-0",
        day_button:
          "h-8 w-8 inline-flex items-center justify-center rounded hover:bg-muted aria-selected:bg-primary aria-selected:text-primary-foreground",
        selected: "[&_button]:bg-primary [&_button]:text-primary-foreground",
        today: "[&_button]:font-bold [&_button]:underline",
        outside: "text-muted-foreground/40",
        disabled: "opacity-40 pointer-events-none",
        hidden: "invisible",
        ...classNames,
      }}
      components={{
        Chevron: ({ orientation }) =>
          orientation === "left" ? (
            <ChevronLeft className="h-4 w-4" />
          ) : (
            <ChevronRight className="h-4 w-4" />
          ),
      }}
      {...props}
    />
  );
}
