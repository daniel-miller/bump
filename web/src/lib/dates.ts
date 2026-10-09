function pad(n: number): string {
  return String(n).padStart(2, "0");
}

/**
 * Today as yyyy-mm-dd, in the reader's own timezone.
 *
 * For seeding a date input, which only accepts that spelling. Local rather than UTC on purpose: an
 * operator in Edmonton preparing a dispatch at six in the evening means today, and toISOString
 * would hand them tomorrow.
 */
export function today(): string {
  const d = new Date();

  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * ISO 8601 in local time with an explicit timezone name:
 * "2026-07-16 14:32 MDT". Explicit padding rather than a locale trick -
 * toLocaleDateString varies per browser and M/D/Y is ambiguous.
 */
export function formatAbsolute(s: string): string {
  const d = new Date(s);
  const stamp = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  const tz = new Intl.DateTimeFormat("en-US", { timeZoneName: "short" })
    .formatToParts(d)
    .find((p) => p.type === "timeZoneName")?.value;
  return tz ? `${stamp} ${tz}` : stamp;
}

/**
 * The date alone, ISO 8601: "2026-07-16". For values where the time of day carries no meaning -
 * an issue date, an expiry - so a column of them stays scannable instead of repeating a
 * midnight timestamp on every row.
 */
export function formatDate(s: string): string {
  const d = new Date(s);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * The time alone with its zone: "10:35 MDT". For a value whose date is shown separately, so the
 * date can lead and the audit detail survives underneath it rather than being dropped.
 */
export function formatTime(s: string): string {
  const d = new Date(s);
  const stamp = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
  const tz = new Intl.DateTimeFormat("en-US", { timeZoneName: "short" })
    .formatToParts(d)
    .find((p) => p.type === "timeZoneName")?.value;
  return tz ? `${stamp} ${tz}` : stamp;
}

export function formatRelative(s: string): string {
  const now = Date.now();
  const then = new Date(s).getTime();
  const diffSec = Math.round((now - then) / 1000);
  const abs = Math.abs(diffSec);
  const rtf = new Intl.RelativeTimeFormat("en", { numeric: "auto" });

  const units: Array<[Intl.RelativeTimeFormatUnit, number]> = [
    ["year", 60 * 60 * 24 * 365],
    ["month", 60 * 60 * 24 * 30],
    ["week", 60 * 60 * 24 * 7],
    ["day", 60 * 60 * 24],
    ["hour", 60 * 60],
    ["minute", 60],
    ["second", 1],
  ];

  for (const [unit, seconds] of units) {
    if (abs >= seconds || unit === "second") {
      const value = Math.round(diffSec / seconds);
      return rtf.format(-value, unit);
    }
  }
  return "";
}
