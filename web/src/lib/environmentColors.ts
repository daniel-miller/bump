// Canonical color legend for environment categories. Mapped by handle or alias
// so admin pages, status boards, and service rows all paint the same swatch
// for the same environment.

export const ENVIRONMENT_COLORS = {
  local: "#4A90D9",
  development: "#2ECC71",
  sandbox: "#F5A623",
  production: "#E74C3C",
  other: "#5D6472",
} as const;

export type EnvironmentCategory = keyof typeof ENVIRONMENT_COLORS;

const LOCAL_TAGS = new Set(["local", "work"]);
const DEVELOPMENT_TAGS = new Set(["development", "dev", "test", "qa", "uat"]);
// "demo" is deliberately absent: since the 2026-07-31 roster rename it names
// the demonstration environment (gray "other"), not the pre-production gate.
const SANDBOX_TAGS = new Set(["sandbox", "stage", "staging"]);
const PRODUCTION_TAGS = new Set(["production", "prod", "live"]);

export function categorizeEnvironment(
  handle: string,
  aliases: readonly string[] = [],
): EnvironmentCategory {
  const tags = [handle, ...aliases].map((t) => t.toLowerCase());
  for (const t of tags) {
    if (LOCAL_TAGS.has(t)) return "local";
    if (DEVELOPMENT_TAGS.has(t)) return "development";
    if (SANDBOX_TAGS.has(t)) return "sandbox";
    if (PRODUCTION_TAGS.has(t)) return "production";
  }
  return "other";
}

export function environmentColor(handle: string, aliases: readonly string[] = []): string {
  return ENVIRONMENT_COLORS[categorizeEnvironment(handle, aliases)];
}
