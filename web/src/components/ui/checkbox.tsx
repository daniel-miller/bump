import * as React from "react";
import * as CheckboxPrimitive from "@radix-ui/react-checkbox";
import { cn } from "@/lib/cn";

export const Checkbox = React.forwardRef<
  React.ElementRef<typeof CheckboxPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof CheckboxPrimitive.Root>
>(({ className, ...props }, ref) => (
  <CheckboxPrimitive.Root
    ref={ref}
    className={cn(
      "peer border-muted-foreground/70 h-4 w-4 shrink-0 rounded-[4px] border shadow-xs transition-shadow outline-none",
      "focus-visible:border-primary focus-visible:ring-primary/50 focus-visible:ring-2 focus-visible:ring-offset-0",
      "data-[state=checked]:bg-primary data-[state=checked]:text-primary-foreground data-[state=checked]:border-primary",
      "data-[state=indeterminate]:bg-primary data-[state=indeterminate]:text-primary-foreground data-[state=indeterminate]:border-primary",
      "disabled:cursor-not-allowed disabled:opacity-50",
      className,
    )}
    {...props}
  >
    {/* The indicator carries the same data-state as the root, so CSS picks the glyph: a dash for
        indeterminate, which would otherwise read as checked. Font Awesome sets display outside
        any layer, so Tailwind's hidden cannot win against it; its --fa-display variable can. */}
    <CheckboxPrimitive.Indicator className="group flex h-full w-full items-center justify-center text-current">
      <i
        className="fa-sharp fa-solid fa-check text-[10px] group-data-[state=indeterminate]:[--fa-display:none]"
        aria-hidden="true"
      />
      <i
        className="fa-sharp fa-solid fa-minus text-[10px] [--fa-display:none] group-data-[state=indeterminate]:[--fa-display:inline-block]"
        aria-hidden="true"
      />
    </CheckboxPrimitive.Indicator>
  </CheckboxPrimitive.Root>
));
Checkbox.displayName = CheckboxPrimitive.Root.displayName;
