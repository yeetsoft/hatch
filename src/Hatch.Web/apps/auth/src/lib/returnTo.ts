/** The sign-in endpoint the Google button points at. */
export const GOOGLE_START = '/api/auth/google/start';

/** Where an approved person lands when nothing said where they were going. */
export const DEFAULT_LANDING = '/apps/hatch/';

/**
 * A rooted, same-origin path, or null. The same rule as the server's
 * SafeReturnTo (AuthChallenge.cs): non-empty, starts with a single `/`, and the
 * second character is neither `/` nor `\` - either of which a browser reads as
 * a host. The server re-checks at /api/auth/google/start, so this is depth.
 */
export function safeReturnTo(raw: string | null): string | null {
  if (!raw || raw[0] !== '/') return null;
  if (raw.length > 1 && (raw[1] === '/' || raw[1] === '\\')) return null;
  return raw;
}

/** The Google button's href, carrying `r` only when it is safe to. */
export function signInHref(r: string | null): string {
  const safe = safeReturnTo(r);
  return safe ? `${GOOGLE_START}?r=${encodeURIComponent(safe)}` : GOOGLE_START;
}
