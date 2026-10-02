/* The pure arithmetic behind a build stamp, the way lib/claim.ts is the pure
   arithmetic behind a claim: nothing here renders, and nothing here reads a
   clock or makes a call. HA-207's row is the first caller; it draws what these
   functions say. */

import type { ClientRevisionVerdict } from '../types';
import { agoPhrase } from './claim';

/** What an unstamped build answers on both sides of the wire - kept identical
    to HatchRevision.Development on the server and to the Vite plugin's own
    DEV_REVISION, so a local build reads the same word wherever it is drawn. */
export const DEVELOPMENT_REVISION = 'dev';

/** False for a local `dotnet build`/`npm run dev`, true for anything a
    pipeline stamped. The one fact the sha and the build time both key off. */
export function isStamped(revision: string): boolean {
  return revision !== DEVELOPMENT_REVISION;
}

/** The seven characters a row has room for. Deliberately not `revision.length
    === 40` to decide whether to slice: HatchRevision.ParseRevision already
    rejects anything that isn't a full sha and answers `'dev'` instead, so this
    layer never sees a truncated one - it only has to not slice `'dev'` itself,
    which has no sha to shorten. */
export function shortSha(revision: string): string {
  return isStamped(revision) ? revision.slice(0, 7) : revision;
}

/** The absolute instant, in the reader's own locale, and the relative form
    beside it - one object so a caller can draw either half without re-deriving
    it. Null whenever there is nothing to say: an unstamped build has no build
    time worth reading even though `ReadBuiltAt` on the server usually hands it
    one (a local `dotnet build` has a last-write time same as a published
    image), and a null `builtAt` is its own, independent reason to say
    nothing. */
export interface BuildTimePhrase {
  absolute: string;
  relative: string;
}

export function buildTimePhrase(
  revision: string,
  builtAt: string | null,
  now: Date,
  locale?: string,
): BuildTimePhrase | null {
  if (!isStamped(revision) || !builtAt) return null;

  const at = new Date(builtAt);
  return {
    // Explicit options rather than a bare toLocaleString() - lib/spend.ts's
    // bucketLabel is the precedent. No seconds: the value is an assembly's
    // last-write time, approximate by construction, and seconds would claim a
    // precision it doesn't have.
    absolute: `${at.toLocaleDateString(locale, {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    })}, ${at.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })}`,
    relative: agoPhrase(builtAt, now),
  };
}

/** Whether a client's verdict earns the "a newer build is live" notice.
    `Behind` alone - never `Ahead`. During a rolling deploy a page loaded from
    a new replica can have its next request answered by an old one, and a
    client that reloads on any difference thrashes between the two until the
    rollout finishes; only being behind is ever actionable. */
export function earnsRevisionNotice(client: ClientRevisionVerdict | null): boolean {
  return client?.drift === 'Behind';
}
