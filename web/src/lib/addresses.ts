/**
 * The email addresses in pasted text, in order, without repeats.
 *
 * Accepts addresses separated by commas, semicolons, spaces, or new lines, and the
 * Outlook form `Jane Doe <jane@example.com>; John <john@example.com>` copied from an
 * email header. The address inside angle brackets wins; the display name around it is
 * dropped, along with any other word that has no @ in it. Nothing is validated here:
 * a malformed address is passed through so the API can say what is wrong with it.
 */
export function parseAddresses(text: string): string[] {
  const found: string[] = [];
  const rest = text.replace(/<([^<>]*)>/g, (_, inner: string) => {
    found.push(inner.trim());
    return " ";
  });
  for (const token of rest.split(/[\s,;]+/)) {
    if (token.includes("@")) found.push(token.replace(/^["']+|["']+$/g, ""));
  }

  const seen = new Set<string>();
  return found.filter((address) => {
    const key = address.toLowerCase();
    if (!address || seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
