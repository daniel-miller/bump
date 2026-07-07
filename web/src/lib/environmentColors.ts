// Canonical color legend for environment categories. Mapped by slug or alias
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
const SANDBOX_TAGS = new Set(["sandbox", "staging", "demo"]);
const PRODUCTION_TAGS = new Set(["production", "prod", "live"]);

export function categorizeEnvironment(slug: string, aliases: readonly string[] = []): EnvironmentCategory {
  const tags = [slug, ...aliases].map((t) => t.toLowerCase());
  for (const t of tags) {
    if (LOCAL_TAGS.has(t)) return "local";
    if (DEVELOPMENT_TAGS.has(t)) return "development";
    if (SANDBOX_TAGS.has(t)) return "sandbox";
    if (PRODUCTION_TAGS.has(t)) return "production";
  }
  return "other";
}

export function environmentColor(slug: string, aliases: readonly string[] = []): string {
  return ENVIRONMENT_COLORS[categorizeEnvironment(slug, aliases)];
}
