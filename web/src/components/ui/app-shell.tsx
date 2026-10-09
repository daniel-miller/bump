import * as React from "react";
import { Slot, Slottable } from "@radix-ui/react-slot";
import { Button, type ButtonProps } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { cn } from "@/lib/cn";

/** The drawer's open state, shared by AppShell, SidebarSheet and SidebarTrigger. */
const DrawerContext = React.createContext<{
  open: boolean;
  setOpen: (open: boolean) => void;
} | null>(null);

/** Tells a Sidebar it is inside SidebarSheet, so it fills the drawer instead of sizing itself. */
const InDrawerContext = React.createContext(false);

/**
 * The frame an admin app puts its pages in: a tinted sidebar beside a white content area.
 *
 * The warm background tint sits on the sidebar, so navigation reads as chrome rather than as the
 * brightest plane on screen, and the content area is white, so cards separate by their hairline
 * borders instead of fighting a tinted page for contrast. In dark mode the same two tokens keep the
 * sidebar one step darker than the content. This is variant 1b of the shell design handoff.
 *
 * Presentation only. Each app keeps its own routing, auth and menus. SidebarNavItem takes the
 * router's NavLink through asChild and styles the active item from aria-current="page", which
 * NavLink sets, so nothing here imports a router.
 *
 * Below md the sidebar moves into a drawer. Render it twice, once hidden below md and once inside
 * SidebarSheet, and put SidebarTrigger first in the Topbar to open the drawer.
 *
 *   <AppShell>
 *     <SkipLink />
 *     <Sidebar className="hidden md:flex">
 *       <SidebarBrand>...</SidebarBrand>
 *       <SidebarNav>
 *         <SidebarNavItem asChild icon="fa-sharp fa-regular fa-gauge">
 *           <NavLink to="/dashboard">Dashboard</NavLink>
 *         </SidebarNavItem>
 *       </SidebarNav>
 *     </Sidebar>
 *     <SidebarSheet>
 *       <Sidebar>...the same brand and nav...</Sidebar>
 *     </SidebarSheet>
 *     <ShellBody>
 *       <Topbar><SidebarTrigger />...</Topbar>
 *       <ShellMain><Outlet /></ShellMain>
 *     </ShellBody>
 *   </AppShell>
 */
export function AppShell({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  const [open, setOpen] = React.useState(false);
  const drawer = React.useMemo(() => ({ open, setOpen }), [open]);

  // The whole shell sits inside the drawer's dialog root, so SidebarTrigger in the Topbar can be a
  // real dialog trigger, and focus returns to it when the drawer closes.
  return (
    <DrawerContext.Provider value={drawer}>
      <Sheet open={open} onOpenChange={setOpen}>
        <div className={cn("flex h-full", className)} {...props} />
      </Sheet>
    </DrawerContext.Provider>
  );
}

/** The first focusable element on the page, so a keyboard can jump past the sidebar. */
export function SkipLink({
  className,
  href = "#main",
  children = "Skip to content",
  ...props
}: React.AnchorHTMLAttributes<HTMLAnchorElement>) {
  return (
    <a
      href={href}
      className={cn(
        "focus:bg-primary focus:text-primary-foreground sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:rounded focus:px-3 focus:py-2 focus:text-sm",
        className,
      )}
      {...props}
    >
      {children}
    </a>
  );
}

export function Sidebar({ className, ...props }: React.HTMLAttributes<HTMLElement>) {
  const inDrawer = React.useContext(InDrawerContext);
  return (
    <aside
      aria-label="Sidebar"
      className={cn(
        "bg-background border-border flex w-56 shrink-0 flex-col border-r",
        inDrawer && "h-full w-full border-r-0",
        className,
      )}
      {...props}
    />
  );
}

/**
 * The sidebar as a left drawer below md, where the Sidebar beside the content is hidden. Its child
 * is a second Sidebar, which fills the drawer. Following any link inside closes the drawer, so the
 * drawer needs no router and the nav items need no close handler.
 */
export function SidebarSheet({
  title = "Menu",
  className,
  children,
  onClick,
  ...props
}: React.ComponentPropsWithoutRef<typeof SheetContent> & {
  /** Read by screen readers as the drawer's name; never shown. */
  title?: string;
}) {
  const { open, setOpen } = useDrawer("SidebarSheet");

  // Widening the window past md hides the drawer but not its overlay, which would leave the page
  // dimmed and inert, so close the drawer instead. 48rem is Tailwind's md.
  React.useEffect(() => {
    if (!open) return;
    const wide = window.matchMedia("(min-width: 48rem)");
    const close = () => {
      if (wide.matches) setOpen(false);
    };
    wide.addEventListener("change", close);
    return () => wide.removeEventListener("change", close);
  }, [open, setOpen]);

  return (
    <SheetContent
      side="left"
      aria-describedby={undefined}
      className={cn("w-64 p-0 md:hidden", className)}
      onClick={(event) => {
        onClick?.(event);
        if (event.target instanceof Element && event.target.closest("a[href]")) setOpen(false);
      }}
      {...props}
    >
      <SheetTitle className="sr-only">{title}</SheetTitle>
      <InDrawerContext.Provider value={true}>{children}</InDrawerContext.Provider>
    </SheetContent>
  );
}

/** The Topbar's menu button that opens SidebarSheet. It hides itself from md up. */
export const SidebarTrigger = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, ...props }, ref) => {
    useDrawer("SidebarTrigger");
    return (
      <SheetTrigger asChild>
        <Button
          ref={ref}
          variant="ghost"
          size="icon"
          aria-label="Open menu"
          className={cn("h-8 w-8 md:hidden", className)}
          {...props}
        >
          <i className="fa-sharp fa-regular fa-bars" aria-hidden="true" />
        </Button>
      </SheetTrigger>
    );
  },
);
SidebarTrigger.displayName = "SidebarTrigger";

function useDrawer(component: string) {
  const drawer = React.useContext(DrawerContext);
  if (!drawer) throw new Error(`${component} must be rendered inside AppShell.`);
  return drawer;
}

/** The wordmark row, as tall as the Topbar so the two bottom hairlines line up. */
export function SidebarBrand({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "border-border flex h-12 shrink-0 items-center border-b px-4 text-xl font-bold tracking-tight",
        className,
      )}
      {...props}
    />
  );
}

export function SidebarNav({ className, ...props }: React.HTMLAttributes<HTMLElement>) {
  return (
    <nav
      aria-label="Primary"
      className={cn("flex flex-1 flex-col overflow-y-auto p-2", className)}
      {...props}
    />
  );
}

export interface SidebarNavItemProps extends React.AnchorHTMLAttributes<HTMLAnchorElement> {
  /**
   * Font Awesome classes for a leading icon, such as "fa-sharp fa-regular fa-server". Optional, but
   * give every item at one level an icon or none of them: a list with a few gaps reads as broken.
   */
  icon?: string;
  /** Render the child, usually the router's NavLink, in place of a plain anchor. */
  asChild?: boolean;
}

/**
 * Active is a soft primary fill with primary text and a 2px primary left rule, not a solid pill: a
 * filled pill is too loud against the tinted panel. The child's className must be a string, since
 * asChild merges it; a NavLink className function would be lost.
 */
export const SidebarNavItem = React.forwardRef<HTMLAnchorElement, SidebarNavItemProps>(
  ({ className, icon, asChild = false, children, ...props }, ref) => {
    const Comp = asChild ? Slot : "a";
    return (
      <Comp
        ref={ref}
        className={cn(
          "group text-foreground hover:bg-muted focus-visible:ring-primary flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors focus-visible:ring-2 focus-visible:outline-none",
          "aria-[current=page]:bg-primary/12 aria-[current=page]:hover:bg-primary/12 aria-[current=page]:text-primary aria-[current=page]:font-semibold aria-[current=page]:shadow-[inset_2px_0_0_0_var(--color-primary)]",
          className,
        )}
        {...props}
      >
        {icon && (
          <i
            className={cn(icon, "fa-fw opacity-75 group-aria-[current=page]:opacity-100")}
            aria-hidden="true"
          />
        )}
        <Slottable>{children}</Slottable>
      </Comp>
    );
  },
);
SidebarNavItem.displayName = "SidebarNavItem";

/** A heading over a group of nav items. Pass asChild with a button to make the group collapsible. */
export function SidebarGroupLabel({
  className,
  asChild = false,
  ...props
}: React.HTMLAttributes<HTMLDivElement> & { asChild?: boolean }) {
  const Comp = asChild ? Slot : "div";
  return (
    <Comp
      className={cn(
        "text-muted-foreground/70 px-3 pt-4 pb-1 text-xs font-medium tracking-wide uppercase",
        className,
      )}
      {...props}
    />
  );
}

/** Pinned under the nav, such as a customer's logo. */
export function SidebarFooter({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("border-border shrink-0 border-t px-4 py-5", className)} {...props} />;
}

/** The column beside the sidebar that holds the Topbar over the ShellMain. */
export function ShellBody({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("flex min-w-0 flex-1 flex-col", className)} {...props} />;
}

/** White like the content under it, so its bottom hairline is what separates the two. */
export function Topbar({ className, ...props }: React.HTMLAttributes<HTMLElement>) {
  return (
    <header
      className={cn(
        "border-border bg-card flex h-12 shrink-0 items-center gap-2 border-b px-3",
        className,
      )}
      {...props}
    />
  );
}

/**
 * The page's own scroll region. relative so absolutely positioned descendants, such as sr-only
 * spans, resolve inside it rather than against the document, where they add a second scrollbar.
 */
export function ShellMain({ className, ...props }: React.HTMLAttributes<HTMLElement>) {
  return (
    <main id="main" className={cn("bg-card relative flex-1 overflow-auto", className)} {...props} />
  );
}
