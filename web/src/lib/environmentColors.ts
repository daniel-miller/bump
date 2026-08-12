// Canonical color legend for environment categories. Mapped by handle or alias
// so admin pages, status boards, and service rows all paint the same swatch
// for the same environment.
//
// The palette is canon in daniel-miller/infra/README.md ("Environment
// colors"); cmds-app/platform/web/src/lib/environments.ts carries the same
// values. Keep this file in step with those, not ahead of them.
//
// The ramp says how far a mistake travels, not whether a thing is healthy.
// Green left it on 2026-08-12 because admins read it as a traffic light: green
// means "passing" and "healthy" everywhere else in the app, and the green/red
// pair it made with live gave a red-green color blind operator no signal.
// Violet work, blue test, amber stage, then live with no hue. Amber sits at
// the loud end because stage is the one that looks like production without
// being it.

export const ENVIRONMENT_COLORS = {
  work: "rgb(150, 122, 214)",
  test: "rgb(74, 144, 217)",
  stage: "rgb(245, 166, 35)",
  // Theme-aware token rather than a literal: a near-black swatch disappears
  // against a dark card. See --color-env-live in styles/globals.css. Red left
  // here on 2026-08-12 - it read as "stop", and it competed with the danger
  // color marking destructive actions on the same screen.
  live: "var(--color-env-live)",
  // Cold, echo, and demo share one gray. Unmapped handles land here too: a
  // token nobody has categorized should look unremarkable rather than borrow a
  // lifecycle color.
  special: "rgb(93, 100, 114)",
} as const;

export type EnvironmentCategory = keyof typeof ENVIRONMENT_COLORS;

const WORK_TAGS = new Set(["work", "local"]);
const TEST_TAGS = new Set(["test", "dev", "development", "qa", "uat"]);
// "demo" is deliberately absent: since the 2026-07-31 roster rename it names
// the demonstration environment (gray "special"), not the pre-production gate.
const STAGE_TAGS = new Set(["stage", "sandbox", "staging"]);
const LIVE_TAGS = new Set(["live", "prod", "production"]);

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
  }
  return "special";
}

export function environmentColor(handle: string, aliases: readonly string[] = []): string {
  return ENVIRONMENT_COLORS[categorizeEnvironment(handle, aliases)];
}
