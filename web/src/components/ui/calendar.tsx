import { DayPicker, type DayPickerProps } from "react-day-picker";
import { cn } from "@/lib/cn";
import "react-day-picker/style.css";

export type CalendarProps = DayPickerProps;

export function Calendar({
  className,
  classNames,
  captionLayout,
  formatters,
  ...props
}: CalendarProps) {
  // In dropdown mode react-day-picker still renders the text month/year label beside the
  // dropdowns; hide it so only the dropdowns show.
  const usingDropdown = typeof captionLayout === "string" && captionLayout.startsWith("dropdown");

  return (
    <DayPicker
      showOutsideDays
      captionLayout={captionLayout}
      // Short month names in the dropdown: "September" and a year select are wider than the
      // space between the two arrows, so the arrows overlapped the selects.
      formatters={{
        ...(usingDropdown && {
          formatMonthDropdown: (month: Date) => month.toLocaleString("en", { month: "short" }),
        }),
        ...formatters,
      }}
      className={cn("p-2", className)}
      classNames={{
        months: "flex flex-col space-y-2",
        month: "space-y-2",
        month_caption: "flex justify-center pt-1 relative items-center",
        caption_label: usingDropdown ? "hidden" : "text-sm font-medium",
        nav: "flex items-center justify-between absolute inset-x-1 top-1",
        button_previous:
          "h-7 w-7 inline-flex items-center justify-center rounded-lg hover:bg-muted",
        button_next: "h-7 w-7 inline-flex items-center justify-center rounded-lg hover:bg-muted",
        dropdowns: "flex items-center justify-center gap-1.5",
        dropdown_root: "relative",
        dropdown: "border-border bg-card rounded-md border px-1.5 py-1 text-sm",
        month_grid: "w-full border-collapse",
        weekdays: "flex",
        weekday: "text-muted-foreground w-8 text-xs font-normal text-center",
        week: "flex w-full mt-1",
        day: "h-8 w-8 text-center text-sm p-0",
        day_button:
          "h-8 w-8 inline-flex items-center justify-center rounded-lg hover:bg-muted aria-selected:bg-primary aria-selected:text-primary-foreground",
        selected: "[&_button]:bg-primary [&_button]:text-primary-foreground",
        today: "[&_button]:font-bold [&_button]:underline",
        outside: "text-muted-foreground/40",
        disabled: "opacity-40 pointer-events-none",
        hidden: "invisible",
        ...classNames,
      }}
      components={{
        Chevron: ({ orientation }) => (
          <i
            className={cn(
              "fa-sharp fa-regular",
              orientation === "left" ? "fa-chevron-left" : "fa-chevron-right",
            )}
            aria-hidden="true"
          />
        ),
      }}
      {...props}
    />
  );
}
