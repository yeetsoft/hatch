/* The console's keys, listed from numbers before anybody has been asked.

   A project says the highest issue number it has handed out
   (`lastIssueNumber`), so for a typed number or key prefix the keys that exist
   are known without a request: `455` is issue 455 of every project that got that
   far. The page lists them at once, titles the ones whose card it holds, and
   leaves a placeholder for the rest until the server's one answer arrives.

   The rules here are the server's (GoToController, GoToQuery) and goTo.ts's own,
   so that what is listed and what comes back agree. In a module with no React in
   it for the reason goTo.ts is: the rule goes in `lib/` and is tested in node. */

import {
  GO_TO_LIMIT,
  compareByNumber,
  goToFound,
  keyMarks,
  prefixMarks,
  squash,
} from './goTo';
import type { GoToRow, Mark } from './goTo';
import { splitKey } from './issuePicker';
import type { GoToRowDto, IssueCard, Project } from '../types';

/** A row of the console: a key, with whatever is known about its issue. */
export interface GoToEntry {
  key: string;
  projectKey: string;
  number: number;
  /** The card the page already holds. Wins over `looked`: it is the live one. */
  card: IssueCard | undefined;
  /** The row the server answered with. Neither this nor a card: a placeholder. */
  looked: GoToRowDto | undefined;
  /** Which of the issue's two strings the marks index into. */
  field: 'key' | 'title';
  marks: Mark[];
  /** Listed only because its whole key was typed, outside the tab's scope. */
  outOfScope: boolean;
}

export interface GoToListing {
  /** At most `GO_TO_LIMIT`. */
  entries: GoToEntry[];
  /** How many matched, before the cap. Exact for keys: the numbers say how many. */
  total: number;
}

/** What the last answer was to, kept for deciding whether the next query needs one. */
export interface GoToAnswered {
  /** The query asked, trimmed. */
  query: string;
  /** The scope asked, as `scopeToken` writes it. */
  scope: string;
  more: boolean;
  rows: GoToRowDto[];
}

/** The scope as one string, for comparing two. `[]` is every project. */
export const scopeToken = (ids: number[]): string => [...ids].sort((a, b) => a - b).join(',');

/** A run of numbers one project has handed out: `lo` to `hi`, both inclusive. */
interface Run {
  project: string;
  lo: number;
  hi: number;
}

/** The numbers up to `last` whose digits start with `tail`, as runs by digit
    length. Never walks the numbers: `last` may be millions. */
function prefixRuns(tail: string, last: number): Array<[number, number]> {
  if (!/^[1-9]\d*$/.test(tail) || tail.length > 10 || last < 1) return [];

  const head = Number(tail);
  const runs: Array<[number, number]> = [];
  for (let width = 1; head * width <= last; width *= 10) {
    runs.push([head * width, Math.min(head * width + width - 1, last)]);
  }
  return runs;
}

/** The runs a project contributes to a query: digits alone are a number prefix
    in every project; a word is every number when it starts the project's key,
    else a number prefix when the project's key starts the word. */
function runsFor(project: Project, typed: string): Run[] {
  const last = project.lastIssueNumber;
  if (last < 1) return [];

  if (/^\d+$/.test(typed)) {
    return prefixRuns(typed, last).map(([lo, hi]) => ({ project: project.key, lo, hi }));
  }

  const wanted = squash(typed);
  const own = squash(project.key);
  if (!wanted || !own) return [];
  if (own.startsWith(wanted)) return [{ project: project.key, lo: 1, hi: last }];
  if (!wanted.startsWith(own)) return [];

  return prefixRuns(wanted.slice(own.length), last).map(([lo, hi]) => ({ project: project.key, lo, hi }));
}

/** Whole keys outside the scope that the word names, which the console lists
    first. `ha34` is HA-34 and HA3-4: each only when that project has got that far. */
function outsideKeys(outside: Project[], typed: string): Array<{ project: string; number: number }> {
  if (/\s/.test(typed) || /^\d+$/.test(typed)) return [];

  const wanted = squash(typed);
  const found: Array<{ project: string; number: number }> = [];
  for (const project of outside) {
    const own = squash(project.key);
    if (!own || !wanted.startsWith(own)) continue;

    const tail = wanted.slice(own.length);
    if (!/^[1-9]\d*$/.test(tail) || tail.length > 10) continue;
    const number = Number(tail);
    if (number <= project.lastIssueNumber) found.push({ project: project.key, number });
  }

  return found.sort((a, b) => a.number - b.number || (a.project < b.project ? -1 : a.project > b.project ? 1 : 0));
}

/** The keys a query names according to the projects' numbers. */
export interface KeyListing {
  /** The first keys, out of scope first, then by number, then by project. */
  keys: Array<{ key: string; projectKey: string; number: number; outOfScope: boolean }>;
  /** How many keys the numbers name. Exact. */
  total: number;
  /** Whether the numbers name this key, whatever the cap kept. */
  has(projectKey: string, number: number): boolean;
}

/**
 * The keys that exist for a query, from `projects` (the tab's scope) and
 * `allProjects` (for a whole key outside it), at most `limit` of them.
 *
 * Counts and walks by digit-length run: `4` over a project at 1,200,000 is four
 * runs, not a loop. The walk takes the least key of every run's cursor, fifty
 * times, which is the order every reading of a query is in.
 */
export function listedKeys(
  projects: Project[],
  allProjects: Project[],
  query: string,
  limit: number = GO_TO_LIMIT,
): KeyListing {
  const typed = query.trim();
  const none: KeyListing = { keys: [], total: 0, has: () => false };
  if (!typed || /\s/.test(typed)) return none;

  const runs = projects.flatMap((project) => runsFor(project, typed));
  const inside = new Set(projects.map((project) => project.key));
  const outside = outsideKeys(
    allProjects.filter((project) => !inside.has(project.key)),
    typed,
  );

  const keys: KeyListing['keys'] = outside.map((o) => ({
    key: `${o.project}-${o.number}`,
    projectKey: o.project,
    number: o.number,
    outOfScope: true,
  }));

  const cursors = runs.map((run) => ({ run, next: run.lo }));
  while (keys.length < limit + outside.length) {
    let best: (typeof cursors)[number] | undefined;
    for (const cursor of cursors) {
      if (cursor.next > cursor.run.hi) continue;
      if (
        !best ||
        cursor.next < best.next ||
        (cursor.next === best.next && cursor.run.project < best.run.project)
      ) {
        best = cursor;
      }
    }
    if (!best) break;

    keys.push({
      key: `${best.run.project}-${best.next}`,
      projectKey: best.run.project,
      number: best.next,
      outOfScope: false,
    });
    best.next += 1;
  }

  const counted = runs.reduce((sum, run) => sum + (run.hi - run.lo + 1), 0) + outside.length;
  return {
    keys: keys.slice(0, limit),
    total: counted,
    has: (projectKey, number) =>
      outside.some((o) => o.project === projectKey && o.number === number) ||
      runs.some((run) => run.project === projectKey && number >= run.lo && number <= run.hi),
  };
}

const ordered = (a: GoToEntry, b: GoToEntry): number =>
  Number(b.outOfScope) - Number(a.outOfScope) || compareByNumber(a.key, b.key);

/**
 * What the console lists for a query before the server has been asked: the keys
 * the projects' numbers name, titled from the cards in hand, then the cards whose
 * title holds the words. At most `GO_TO_LIMIT`, and `total` counts all.
 *
 * Every key a card in hand names is listed too, so a project whose number is
 * stale loses nothing the page can already see.
 */
export function listEntries(args: {
  cards: IssueCard[];
  projects: Project[];
  allProjects: Project[];
  scopeKeys: string[];
  query: string;
}): GoToListing {
  const { cards, projects, allProjects, scopeKeys } = args;
  const typed = args.query.trim();
  if (!typed) return { entries: [], total: 0 };

  const held = new Map(cards.map((card) => [card.key, card]));
  const found = goToFound(cards, typed, scopeKeys);
  const listing = listedKeys(projects, allProjects, typed);

  const entry = (row: GoToRow): GoToEntry => {
    const parts = splitKey(row.card.key);
    return {
      key: row.card.key,
      projectKey: row.card.projectKey,
      number: parts?.index ?? 0,
      card: row.card,
      looked: undefined,
      field: row.field,
      marks: row.marks,
      outOfScope: row.outOfScope,
    };
  };

  const keyEntries: GoToEntry[] = listing.keys.map((k) => ({
    key: k.key,
    projectKey: k.projectKey,
    number: k.number,
    card: held.get(k.key),
    looked: undefined,
    field: 'key',
    marks: keyMarks(k.key, typed) ?? prefixMarks(k.key, squash(typed).length),
    outOfScope: k.outOfScope,
  }));

  const listed = new Set(keyEntries.map((e) => e.key));
  const extras = found
    .filter((row) => row.field === 'key')
    .filter((row) => {
      const parts = splitKey(row.card.key);
      return !listed.has(row.card.key) && !(parts && listing.has(parts.project, parts.index));
    })
    .map(entry);

  const keys = [...keyEntries, ...extras].sort(ordered);
  const keyed = new Set(keys.map((e) => e.key));
  const titles = found.filter((row) => row.field === 'title' && !keyed.has(row.card.key)).map(entry);

  return {
    entries: [...keys.slice(0, GO_TO_LIMIT), ...titles].slice(0, GO_TO_LIMIT),
    total: listing.total + extras.length + titles.length,
  };
}

/** Whether `later` can only find what `earlier` did: both digits alone, or both
    one word, and `later` starts with `earlier` on letters and digits. */
export function narrows(earlier: string, later: string): boolean {
  const a = earlier.trim();
  const b = later.trim();
  if (!a || !b || /\s/.test(a) || /\s/.test(b)) return false;

  const digits = /^\d+$/;
  if (digits.test(a) !== digits.test(b)) return false;
  if (digits.test(a)) return b.startsWith(a);

  const left = squash(a);
  return left !== '' && squash(b).startsWith(left);
}

/**
 * The listing with the server's answer laid over it.
 *
 * - An answer row replaces the entry of the same key, and joins when none was
 *   listed - an issue past the number the page last read.
 * - A listed key with no card that the answer does not have drops out, once the
 *   answer covers its place: it was deleted. An answer covers every key when it
 *   was complete, and else the keys up to its last row; and only for a query it
 *   answered or the new one narrows. A key outside the scope drops only for the
 *   very query that was asked, since a narrower answer is not asked about it.
 * - A card in hand is never dropped: the page can see the issue.
 *
 * An answer to another scope is not for this listing and changes nothing.
 */
export function mergeAnswer(
  listing: GoToListing,
  answered: GoToAnswered | null,
  query: string,
  scope: string,
): GoToListing {
  const typed = query.trim();
  if (!answered || answered.scope !== scope || !typed) return listing;

  const rows = new Map(answered.rows.map((row) => [row.key, row]));
  const narrowed = narrows(answered.query, typed);
  const same = narrowed && squash(answered.query) === squash(typed);

  const inScope = answered.rows.filter((row) => !row.outOfScope);
  const edge = inScope.length > 0 ? inScope[inScope.length - 1].key : null;
  const covers = (key: string): boolean =>
    !answered.more || (edge !== null && compareByNumber(key, edge) <= 0);

  let total = listing.total;
  const kept: GoToEntry[] = [];
  for (const entry of listing.entries) {
    const row = rows.get(entry.key);
    if (row) {
      kept.push({ ...entry, looked: row });
      continue;
    }

    const dropped =
      entry.field === 'key' &&
      !entry.card &&
      (entry.outOfScope ? same : narrowed) &&
      (entry.outOfScope ? !answered.more : covers(entry.key));
    if (dropped) total -= 1;
    else kept.push(entry);
  }

  const have = new Set(kept.map((entry) => entry.key));
  const wanted = squash(typed);
  for (const row of answered.rows) {
    if (have.has(row.key)) continue;

    // A prefix lists no key outside the scope, only the whole key does.
    if (row.outOfScope && squash(row.key) !== wanted) continue;
    const marks = keyMarks(row.key, typed);
    if (!marks) continue;

    kept.push({
      key: row.key,
      projectKey: row.projectKey,
      number: row.number,
      card: undefined,
      looked: row,
      field: 'key',
      marks,
      outOfScope: row.outOfScope,
    });
    total += 1;
  }

  const keys = kept.filter((entry) => entry.field === 'key').sort(ordered);
  const titles = kept.filter((entry) => entry.field === 'title');
  return { entries: [...keys, ...titles].slice(0, GO_TO_LIMIT), total };
}

/**
 * Whether the console must ask the server about `query`.
 *
 * Not when no listed key lacks its card, since there is nothing to learn; not
 * when this query was answered already; and not when a complete answer to a
 * broader query of the same scope is held, which already says what exists.
 */
export function needsLookup(
  entries: GoToEntry[],
  query: string,
  scope: string,
  last: Pick<GoToAnswered, 'query' | 'scope' | 'more'> | null,
): boolean {
  const typed = query.trim();
  if (!typed || entries.every((entry) => entry.card)) return false;

  if (last && last.scope === scope) {
    if (last.query === typed) return false;
    if (!last.more && narrows(last.query, typed)) return false;
  }

  return true;
}
