/** The page number's arithmetic, apart from HistoryPage for the reason every
    other lib file here is: the web test run has no DOM, so what can be tested
    lives where a test can reach it. */

export const PAGE_SIZE = 100;

/** The `?page=` param, 1-based. Missing, unparseable, zero, negative or
    fractional all read as page 1 - only a positive integer is itself. */
export function parsePage(params: URLSearchParams): number {
  const raw = params.get("page");
  if (raw === null) return 1;

  const page = Number(raw);
  return Number.isInteger(page) && page > 0 ? page : 1;
}

export function offsetFor(page: number): number {
  return (page - 1) * PAGE_SIZE;
}

/** What `getActivity` is asked for a page: the offset, and the scope's project
    ids only when there are some - left out when unscoped, so the URL is the
    one sent before scopes existed. */
export function activityRequest(
  page: number,
  ids: number[],
): { offset: number; projectId?: number[] } {
  const offset = offsetFor(page);
  return ids.length > 0 ? { offset, projectId: ids } : { offset };
}

export function historyDescription(scoped: boolean): string {
  return scoped
    ? "What happened last, across the projects in scope, newest first."
    : "What happened last, across every project, newest first.";
}

/** Page 1 is the only page with nothing to go newer to. */
export function hasNewer(page: number): boolean {
  return page > 1;
}
