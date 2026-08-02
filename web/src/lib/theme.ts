// Per-tenant theming for custom-hostname status boards. The API returns a
// token object stored in tenant.tenant_theme; this module turns it into a
// <style> sheet appended after the bundled CSS so its variable values win
// for both the :root (light) and .dark scopes, keeping the theme toggle
// working. Values are validated against strict patterns so a bad row in the
// database cannot inject arbitrary CSS into the page.

export interface TenantTheme {
  font?: string;
  logo?: string;
  favicon?: string;
  radius?: Record<string, string>;
  light?: Record<string, string>;
  dark?: Record<string, string>;
}

const COLOR_KEYS = new Set([
  "background",
  "foreground",
  "foreground-strong",
  "link",
  "muted",
  "muted-foreground",
  "border",
  "card",
  "card-foreground",
  "primary",
  "primary-foreground",
  "success",
  "warning",
  "danger",
]);

const RADIUS_KEYS = new Set(["card", "badge"]);

const COLOR_VALUE = /^(#[0-9a-f]{3,8}|rgba?\([\d\s.,%/]+\))$/i;
const RADIUS_VALUE = /^\d+(\.\d+)?(px|rem)$/;

const FONT_STACKS: Record<string, string> = {
  inter:
    '"Inter Variable", ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif',
};

const FONT_LOADERS: Record<string, () => Promise<unknown>> = {
  inter: () => import("@fontsource-variable/inter"),
};

function colorDeclarations(tokens: Record<string, string> | undefined): string {
  if (!tokens) return "";
  return Object.entries(tokens)
    .filter(([k, v]) => COLOR_KEYS.has(k) && COLOR_VALUE.test(v))
    .map(([k, v]) => `--color-${k}: ${v};`)
    .join(" ");
}

function radiusDeclarations(tokens: Record<string, string> | undefined): string {
  if (!tokens) return "";
  return Object.entries(tokens)
    .filter(([k, v]) => RADIUS_KEYS.has(k) && (RADIUS_VALUE.test(v) || v === "9999px"))
    .map(([k, v]) => `--radius-${k}: ${v};`)
    .join(" ");
}

export function applyTenantTheme(theme: TenantTheme): void {
  const fontStack = theme.font ? FONT_STACKS[theme.font] : undefined;
  const rootDecls = [
    colorDeclarations(theme.light),
    radiusDeclarations(theme.radius),
    fontStack ? `--app-font-sans: ${fontStack};` : "",
  ]
    .filter(Boolean)
    .join(" ");
  const darkDecls = colorDeclarations(theme.dark);

  const css = [rootDecls && `:root { ${rootDecls} }`, darkDecls && `.dark { ${darkDecls} }`]
    .filter(Boolean)
    .join("\n");

  if (css) {
    const style = document.createElement("style");
    style.dataset.tenantTheme = "";
    style.textContent = css;
    document.head.appendChild(style);
  }

  if (theme.font) void FONT_LOADERS[theme.font]?.();

  if (theme.favicon && theme.favicon.startsWith("/")) {
    const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
    if (link) {
      link.href = theme.favicon;
      link.type = theme.favicon.endsWith(".svg") ? "image/svg+xml" : "image/png";
    }
  }
}
