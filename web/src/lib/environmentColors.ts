// Canonical color legend for environment categories. Mapped by handle or alias
// so admin pages, status boards, and service rows all paint the same swatch
// for the same environment.
//
// The palette is canon in daniel-miller/infra/README.md ("Environment
// colors"); cmds-app/platform/web/src/lib/environments.ts carries the same
// values. Keep this file in step with those, not ahead of them.
//
// A swatch carries TWO independent facts on two channels.
//
// Fill is promotion distance: how many gates stand between a change made here
// and production. Work has three, test two, stage one, live none because it is
// production, and the off-ramp environments have no answer at all - nothing in
// cold, echo, or demo can ever be promoted into live, which is what the gray
// means. Violet, blue, amber, then no hue. Amber sits at the loud end because
// stage is the last gate and the one that looks like production without being
// it.
//
// It is not health. Green left the ramp on 2026-08-12 because admins read it as
// a traffic light: green means "passing" and "healthy" everywhere else in the
// app, and the green/red pair it made with live gave a red-green color blind
// operator no signal.
//
// The wording used to be "how far a mistake travels", which carried three
// readings that disagree (a change propagating, the blast radius of a
// destructive action, who sees it). Promotion distance is the one every
// placement here follows.
//
// A ring says the data is copied from production. That channel exists because
// one gray was doing four jobs: cold and echo hold real customer records, demo
// holds data invented to look good, and any uncategorized handle fell in with
// them. The dangerous half was the last one - a misspelled handle rendered
// exactly like a copy of production. Unknown now renders hollow.
//
// Both signals are shape rather than hue, so they survive the same
// color-blindness constraint that removed green.

export const ENVIRONMENT_COLORS = {
  work: "rgb(150, 122, 214)",
  test: "rgb(74, 144, 217)",
  stage: "rgb(245, 166, 35)",
  // Theme-aware token rather than a literal: a near-black swatch disappears
  // against a dark card. See --color-env-live in styles/globals.css. Red left
  // here on 2026-08-12 - it read as "stop", and it competed with the danger
  // color marking destructive actions on the same screen.
  live: "var(--color-env-live)",
  // Cold and echo: descended from live, so they wear the lineage ring.
  derived: "rgb(93, 100, 114)",
  // Demo: same gray, no ring. Descended from nothing, so a hue would wrongly
  // place it on the mistake-travel ramp.
  demo: "rgb(93, 100, 114)",
  // Not in the roster. No fill at all - see environmentSwatchStyle.
  unknown: "transparent",
} as const;

export type EnvironmentCategory = keyof typeof ENVIRONMENT_COLORS;

const WORK_TAGS = new Set(["work", "local"]);
const TEST_TAGS = new Set(["test", "dev", "development", "qa", "uat"]);
// "demo" is deliberately absent here: since the 2026-07-31 roster rename it
// names the demonstration environment, not the pre-production gate.
const STAGE_TAGS = new Set(["stage", "sandbox", "staging"]);
const LIVE_TAGS = new Set(["live", "prod", "production"]);
const DERIVED_TAGS = new Set(["cold", "echo"]);
const DEMO_TAGS = new Set(["demo", "preview", "promo"]);

export function categorizeEnvironment(
  handle: string,
  aliases: readonly string[] = [],
): EnvironmentCategory {
  const tags = [handle, ...aliases].map((t) => t.toLowerCase());
  for (const t of tags) {
    if (WORK_TAGS.has(t)) return "work";
    if (TEST_TAGS.has(t)) return "test";
    if (STAGE_TAGS.has(t)) return "stage";
    if (LIVE_TAGS.has(t)) return "live";
    if (DERIVED_TAGS.has(t)) return "derived";
    if (DEMO_TAGS.has(t)) return "demo";
  }
  // Everything unmatched. Previously this fell in with cold, echo, and demo,
  // which made a typo indistinguishable from a production replica.
  return "unknown";
}

export function environmentColor(handle: string, aliases: readonly string[] = []): string {
  return ENVIRONMENT_COLORS[categorizeEnvironment(handle, aliases)];
}

/** True when this environment's data is copied from production. Drives the ring. */
export function isDerivedFromLive(handle: string, aliases: readonly string[] = []): boolean {
  return categorizeEnvironment(handle, aliases) === "derived";
}

/**
 * Everything a swatch needs, so callers never assemble the two channels
 * themselves and drift apart.
 *
 * The ring is an `outline` with an offset rather than a `border`, so it sits
 * outside the swatch instead of eating into the fill it qualifies. It is drawn
 * in live's own color, which is the whole idea: a gray square wearing live's
 * neutral says "a copy of live" without inventing a hue.
 */
export function environmentSwatchStyle(
  handle: string,
  aliases: readonly string[] = [],
  /**
   * Overrides the handle heuristic. Pass `environment.is_derived_from_live`
   * wherever the row is in hand: the database is authoritative and the tag
   * matching below is only a fallback for places like a service row, where all
   * that exists is a string.
   */
  derivedFromLive?: boolean,
): React.CSSProperties {
  const category = categorizeEnvironment(handle, aliases);
  const ringed = derivedFromLive ?? category === "derived";

  if (category === "unknown" && derivedFromLive !== true) {
    return {
      backgroundColor: "transparent",
      border: "1px dashed color-mix(in oklch, var(--color-muted-foreground) 50%, transparent)",
    };
  }

  return {
    backgroundColor:
      category === "unknown" ? ENVIRONMENT_COLORS.derived : ENVIRONMENT_COLORS[category],
    ...(ringed ? { outline: "1.5px solid var(--color-env-live)", outlineOffset: "1.5px" } : {}),
  };
}

/** Text for a title attribute or screen reader, since the ring is decoration. */
export function environmentSwatchLabel(
  handle: string,
  aliases: readonly string[] = [],
  derivedFromLive?: boolean,
): string {
  const category = categorizeEnvironment(handle, aliases);
  if (derivedFromLive ?? category === "derived") {
    return `${handle}: holds a copy of production data`;
  }
  if (category === "unknown") return `${handle}: not in the environment roster`;
  return handle;
}
