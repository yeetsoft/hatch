/** The page number's arithmetic, apart from HistoryPage for the reason every
    other lib file here is: the web test run has no DOM, so what can be tested
    lives where a test can reach it. */

export const PAGE_SIZE = 100;

/** The `?page=` param, 1-based. Missing, unparseable, zero, negative or
    fractional all read as page 1 - only a positive integer is itself. */
export function parsePage(params: URLSearchParams): number {
  const raw = params.get('page');
  if (raw === null) return 1;

  const page = Number(raw);
  return Number.isInteger(page) && page > 0 ? page : 1;
}

export function offsetFor(page: number): number {
  return (page - 1) * PAGE_SIZE;
}

/** Page 1 is the only page with nothing to go newer to. */
export function hasNewer(page: number): boolean {
  return page > 1;
}
