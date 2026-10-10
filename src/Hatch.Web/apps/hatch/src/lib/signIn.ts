/**
 * What a `fetch` does when the wall refuses it.
 *
 * The gate answers a document navigation with a 302 to the sign-in shell and a
 * fetch with a bare 401, deliberately: a redirect handed to `fetch` is
 * invisible to the caller, which follows it, gets the shell's HTML back with a
 * 200, and reports a JSON parse error somewhere unrelated to authentication
 * (see AuthChallenge in src/Hatch.Api/Services/Auth/AuthChallenge.cs).
 *
 * That leaves the navigating *to* sign-in as the client's job, and nothing was
 * doing it. On 2026-08-22, with the wall newly up, the kiosk tablets sat for
 * hours showing hours-old data: every poll came back 401, every caller treated
 * it as "offline, the next poll covers it", and nothing ever navigated. A
 * revoked grant, or one that lapses past the browser's 400-day cookie cap,
 * produces exactly the same silence - so this is not migration cleanup, it is
 * the missing half of the refusal contract.
 */

/** Where the shell lives. Exempt from the gate by construction, or this is a loop. */
export const SIGN_IN_PATH = '/apps/auth/';

/** One navigation per document. A burst of parallel 401s must not stack them. */
let leaving = false;

/** The shell's address, carrying where the browser is now so redemption lands back here. */
export function signInHref(): string {
  const returnTo = `${location.pathname}${location.search}`;
  return `${SIGN_IN_PATH}?r=${encodeURIComponent(returnTo)}`;
}

/**
 * Sends the browser to sign in, carrying where it was so redemption lands back
 * here. Returns whether it actually started navigating, so a caller can tell
 * "handled, stop rendering" from "not our concern".
 *
 * `location.replace` rather than `assign`: the page being abandoned is one the
 * viewer cannot use, and leaving it in history means Back returns to a dead
 * shell rather than to wherever they came from.
 */
export function redirectToSignIn(): boolean {
  if (leaving) return true;

  // Already on the shell - it is exempt, so this should be unreachable, but a
  // redirect from sign-in to sign-in is the one bug here that has no exit.
  if (location.pathname.startsWith(SIGN_IN_PATH)) return false;

  leaving = true;
  location.replace(signInHref());
  return true;
}

/**
 * The one-liner every fetch caller needs: hands back true when the response was
 * a refusal and the browser is now on its way to sign in.
 *
 * Two refusals mean the same thing. A 401 is no grant at all. A 403 whose body
 * says `pending_approval` is a grant whose person's role was dropped
 * mid-session, so the board is no longer theirs to see. Any other 403 is a
 * refusal of one route and is the caller's to report. The body is read from a
 * clone so the caller can still read the original.
 */
export async function handledRefusal(response: Response): Promise<boolean> {
  if (response.status === 401) return redirectToSignIn();
  if (response.status !== 403) return false;

  try {
    const body = (await response.clone().json()) as { error?: unknown };
    return body?.error === 'pending_approval' && redirectToSignIn();
  } catch {
    return false;
  }
}
