function pad(n: number): string {
  return String(n).padStart(2, "0");
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
