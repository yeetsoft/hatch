/* The scope - which projects the whole app is looking at - as a vocabulary
   apart from React and the router, the way lib/timeline.ts is apart from the
   timeline page: this app has no DOM test setup, so what can be wrong lives
   in a function with a test beside it. Nothing here touches React or the DOM.

   The URL is the truth: `?scope=AER,FIL`, the keys in the project list's own
   order, and no param at all for every project - naming all is naming none.
   `parseScope` reads it against the project list; `rawScope` reads it against
   nothing, for the moments before the list has loaded.

   `keepScope` is the keeper's whole rule, pure: given where the person just
   arrived and what has been carried so far, it says what to carry on and
   whether the location needs a `scope` put back on it. A click on a link that
   names no scope is a PUSH to a bare path, and the rule answers with the
   REPLACE that puts the scope on it, so each click is still one history
   entry. A POP - a document load, Back, Forward - is never re-applied: the
   address alone says what the scope is, and re-applying it on Back would trap
   the person on an entry they cannot leave. */

import type { NavigationType } from 'react-router-dom';

/** The URL name the scope is held under. */
export const SCOPE = 'scope';

/** The `location.state` flag that makes clearing the scope atomic with the
    location it clears on, so a clear can never be re-applied. */
export const SCOPE_CLEARED = 'hatchScopeCleared';

/** The `scope` value split on `,`, trimmed, empties and duplicates dropped, in
    the order written. No project list is consulted: an unknown key stays. */
export function rawScope(params: URLSearchParams): string[] {
  const keys = (params.get(SCOPE) ?? '')
    .split(',')
    .map((key) => key.trim())
    .filter(Boolean);

  return [...new Set(keys)];
}

/** The keys `rawScope` names that are in `projects`, in the project list's own
    order. `[]` - every project - for a single-project list, for a list where
    every key is named, and when nothing is: unknown keys drop silently, and a
    link naming only projects this Hatch does not have is "every project"
    rather than an empty page. */
export function parseScope(params: URLSearchParams, projects: { key: string }[]): string[] {
  if (projects.length < 2) return [];

  const named = new Set(rawScope(params));
  const keys = projects.map((project) => project.key).filter((key) => named.has(key));

  return keys.length === projects.length ? [] : keys;
}

/** A copy of `params` with `scope` set to `keys` in `allKeys` order joined by
    `,` - or removed when `keys` is empty or holds every key in `allKeys`. Every
    other param is kept; the input is never mutated. */
export function withScope(params: URLSearchParams, keys: string[], allKeys: string[]): URLSearchParams {
  const next = new URLSearchParams(params);
  const chosen = allKeys.filter((key) => keys.includes(key));

  if (chosen.length === 0 || chosen.length === allKeys.length) next.delete(SCOPE);
  else next.set(SCOPE, chosen.join(','));

  return next;
}

/** `''`, or `?` and the params with the commas of the `scope` list written as
    commas: `URLSearchParams` encodes them, and the address must read
    `?scope=AER,FIL`. Parsing accepts either form. */
export function scopeSearch(params: URLSearchParams): string {
  const search = params.toString().replaceAll('%2C', ',');

  return search ? `?${search}` : '';
}

/** `path` with `?scope=K1,K2` appended when `keys` is non-empty, else `path`
    unchanged. For the hrefs that live outside the router; the path has no
    query of its own. */
export function scopedPath(path: string, keys: string[]): string {
  return keys.length > 0 ? `${path}${scopeSearch(new URLSearchParams({ [SCOPE]: keys.join(',') }))}` : path;
}

export interface KeepScopeInput {
  location: { pathname: string; search: string; hash: string; state: unknown };
  /** react-router's enum, or the strings it holds. */
  navigationType: `${NavigationType}`;
  /** Raw keys: the list may not have loaded when the first click happens, and
      a carry validated against an empty list would drop the scope. */
  carried: string[];
}

export interface KeepScopeResult {
  carried: string[];
  redirect: { pathname: string; search: string; hash: string } | null;
}

/** What to carry on from `location`, and the location to replace it with when
    the scope has to be put back on it. In order:

    1. The URL names a `scope` (even an empty one): that is the scope.
    2. The location's state says the scope was just cleared: nothing carried.
    3. A POP: nothing carried - the address alone is the truth.
    4. Nothing carried: nothing to do.
    5. Otherwise carry on, and redirect to the same path and hash with `scope`
       set to what is carried, every other param untouched and in its order. */
export function keepScope({ location, navigationType, carried }: KeepScopeInput): KeepScopeResult {
  const params = new URLSearchParams(location.search);

  if (params.has(SCOPE)) return { carried: rawScope(params), redirect: null };

  const state = location.state as Record<string, unknown> | null | undefined;
  if (state?.[SCOPE_CLEARED] === true) return { carried: [], redirect: null };

  if (navigationType === 'POP') return { carried: [], redirect: null };

  if (carried.length === 0) return { carried, redirect: null };

  params.set(SCOPE, carried.join(','));

  return {
    carried,
    redirect: { pathname: location.pathname, search: scopeSearch(params), hash: location.hash },
  };
}
