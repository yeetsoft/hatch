/** What GET /api/auth/me says; every field is null for an unclaimed device. */
export type MeBody = {
  personId: string | null;
  name: string | null;
  role: string | null;
};

export type AuthView =
  | { kind: 'loading' }
  | { kind: 'signed-out' }
  | { kind: 'pending'; name: string | null }
  | { kind: 'redirect' }
  | { kind: 'error' };

/**
 * Which screen a `me` answer earns. Anything unexpected is `error`, never a
 * redirect: bouncing from here to the app, which bounces back here, is a loop.
 */
export function viewFor(status: number, me: MeBody | null): AuthView {
  if (status === 401) return { kind: 'signed-out' };
  if (status !== 200 || !me) return { kind: 'error' };
  switch (me.role?.toLowerCase()) {
    case undefined:
      return { kind: 'signed-out' }; // a grant nobody has claimed
    case 'pending':
      return { kind: 'pending', name: me.name ?? null };
    case 'user':
    case 'admin':
      return { kind: 'redirect' };
    default:
      return { kind: 'error' };
  }
}

/** The sentence a `?error=` code earns, or null when there is no code. */
export function errorMessage(code: string | null): string | null {
  if (!code) return null;
  switch (code) {
    case 'access_denied':
      return 'The sign-in was cancelled.';
    case 'google_not_configured':
      return 'Google sign-in is not set up.';
    case 'email_not_verified':
      return 'The email on that Google account is not verified.';
    case 'unknown_state':
    case 'expired_state':
      return 'The sign-in took too long. Try again.';
    default:
      return `Sign-in failed (${code}).`;
  }
}
