/**
 * Where this bundle is mounted, as far as the browser is concerned: the
 * origin's root (HA-2248). React Router's `basename` has to agree with
 * whatever the address bar is showing, and now there is only one shape to
 * agree with.
 */
export const APP_BASENAME = '/';

export function routerBasename(_pathname: string): string {
  return APP_BASENAME;
}

/**
 * An in-app route as an href the browser can follow on its own.
 *
 * <Link> applies the basename; a raw anchor does not. There were two places
 * that needed a raw anchor unconditionally - the summary dialog's "open in a
 * new tab" button (`IssuePeek.tsx`) and the confirmation chicklet's key link
 * (`Confirmations.tsx`), both for the same reason: `target="_blank"` is the
 * whole point and React Router will not open a second document. In the
 * installed app neither does any more - `useStandalone()` (`lib/viewport.ts`)
 * says there is no second document to open into there, so both are now
 * conditional on it, falling back to a routed <Link> instead.
 */
export function hrefWithin(basename: string, path: string): string {
  return basename === '/' ? path : `${basename}${path}`;
}

/** The same, against whatever address this document was actually served at. */
export const appHref = (path: string): string =>
  hrefWithin(routerBasename(window.location.pathname), path);

/** A full, copyable address for an in-app route, against a given origin and pathname. */
export function fullHref(origin: string, pathname: string, path: string): string {
  return `${origin}${hrefWithin(routerBasename(pathname), path)}`;
}

/**
 * The first segment of every route `App.tsx` mounts, so a link within a docs
 * page or a doc's own prose can be told apart from a link leaving the app
 * without hardcoding either list twice. `docs.test.ts`'s drift guard reads
 * `App.tsx` itself and asserts every one of its routes' first segments is
 * named here.
 */
export const APP_ROUTE_ROOTS = [
  'plan',
  'dreams',
  'reports',
  'search',
  'leaderboard',
  'bulk',
  'projects',
  'statuses',
  'playbooks',
  'design',
  'runners',
  'overnight',
  'changelog',
  'history',
  'issues',
  'import',
  'runner',
  'settings',
  'users',
  'docs',
] as const;

/** Whether `href` is a path this app routes, rather than one the browser is left to handle on its own. */
export function isBoardPath(href: string): boolean {
  if (href === '/') return true;
  if (href[0] !== '/') return false;

  let end = href.length;
  for (const mark of ['/', '?', '#']) {
    const at = href.indexOf(mark, 1);
    if (at !== -1 && at < end) end = at;
  }

  return (APP_ROUTE_ROOTS as readonly string[]).includes(href.slice(1, end));
}
