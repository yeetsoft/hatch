/* The timeline's scale, its tick marks and its today marker, and
   the row fold that turns the answer into drawable rows - with a segment's
   title and colour and the unresolved footnote's sentence - apart
   from the page the way lib/leaderboard.ts and lib/spend.ts are apart from
   theirs: this app has no DOM test setup, so what can be wrong lives in a
   function with a test beside it, and the component is left thin enough to
   read. Nothing here touches React or the DOM.

   One decision is the reason the file exists rather than being helpers inside
   the page: **ticks are boundaries in the reader's offset, not the runtime's.**
   A day tick is the reader's midnight, a week tick their Monday, a month tick
   their 1st - found by shifting the instant by the same `offsetMinutes` the
   rest of the app sends the server, reading it with `getUTC*`, and shifting
   back. Labels are formatted from the shifted date with `timeZone: 'UTC'` for
   the same reason; `dayLabel` formats in the runtime's zone and would name the
   wrong day for anyone whose offset is not the machine's.

   The fold is the same idea as `orderPlan` in lib/plan.ts: the component only
   maps over what `foldRows` returns, and which rows are open is a set of issue
   keys the page keeps in state and `toggled` returns a new copy of.

   It reads `dayLabel` from lib/leaderboard.ts and copies none of it. */

import type { Project, Status, Timeline, TimelineEntry, TimelineSegment } from '../types';
import { statusVars } from './color';
import { dayLabel } from './leaderboard';
import { projectsInScope } from './scope';

/** x is percent of the pane, so the scale is resolution-free and the component
    can place a tick with a `left: x%` or a viewBox of this width. */
export const CHART_UNITS = 100;

const DAY_MS = 86_400_000;
const MINUTE_MS = 60_000;

/** The URL names the two filters hold, beside `from`, `to` and `range`. */
export const PROJECT = 'project';
export const CREATOR = 'creator';

/** The project whose `key` the URL names, or null when it names none, is empty
    or absent. A key, never an id: the key is what a person types or pastes, and
    a number in its place is a typo like any other. */
export function resolveProject(params: URLSearchParams, projects: Project[]): Project | null {
  const key = params.get(PROJECT)?.trim();

  return (key && projects.find((p) => p.key === key)) || null;
}

/** The creator the URL names, `''` for anyone. A name is free text, so it is
    kept as typed apart from its edges. */
export const resolveCreator = (params: URLSearchParams): string => params.get(CREATOR)?.trim() ?? '';

/** `creators` in the order given, with `selected` appended last when it is
    non-empty and not among them, so a pasted link naming someone the list does
    not hold still shows who it filters to. Never sorts; the input is never
    mutated. */
export function creatorChoices(creators: string[], selected: string): string[] {
  return selected && !creators.includes(selected) ? [...creators, selected] : [...creators];
}

/** A copy of `params` with the filter `name` set to `value`, or removed when
    `value` is `''`. Every other param is kept; the input is never mutated. */
export function withFilter(
  params: URLSearchParams,
  name: typeof PROJECT | typeof CREATOR,
  value: string,
): URLSearchParams {
  const next = new URLSearchParams(params);
  if (value) next.set(name, value);
  else next.delete(name);

  return next;
}

/** The projects whose key is among the URL's repeated `project` params
    (trimmed, deduped), in `projects` order. An empty array when a `project`
    param is present and every value is empty - `project=`, the choice of no
    project at all, which `withProjects` writes. Every project when the set is
    otherwise empty: no `project` param at all, or every key given was unknown
    to `projects`. A report that read the chart down to nothing because a link
    named a project this Hatch no longer has would be a confusing kind of
    empty; this one is "every project" instead, and an unknown key mixed with
    a known one is dropped silently, keeping the known one. */
export function resolveProjects(params: URLSearchParams, projects: Project[]): Project[] {
  const given = params.getAll(PROJECT).map((key) => key.trim());
  if (given.length > 0 && given.every((key) => key === '')) return [];

  const keys = new Set(given);
  const chosen = projects.filter((project) => keys.has(project.key));

  return chosen.length > 0 ? chosen : projects;
}

/** A copy of `params` with every `project` entry replaced by one per key in
    `keys`, in `allKeys` order - unless `keys` holds every key in `allKeys`,
    in which case none are written: All is the clean URL, the mirror of
    `resolveProjects`' own fallback to every project. An empty `keys` writes
    one empty `project=`, which `resolveProjects` reads back as no project.
    Every other param is kept; the input is never mutated. */
export function withProjects(params: URLSearchParams, keys: string[], allKeys: string[]): URLSearchParams {
  const next = new URLSearchParams(params);
  next.delete(PROJECT);

  if (keys.length >= allKeys.length) return next;

  if (keys.length === 0) next.append(PROJECT, '');
  else for (const key of allKeys) if (keys.includes(key)) next.append(PROJECT, key);

  return next;
}

/** The reports' empty state when the Projects choice is empty - one wording,
    like `changelogReportEmptyWords`. */
export const noProjectWords = (): string => 'No project is chosen.';

/** `picked` when it is fewer than `all`, else `undefined` - the read every
    project has always been, so a report that names no project asks the server
    for none. */
export function narrowedTo(picked: Project[], all: Project[]): Project[] | undefined {
  return picked.length < all.length ? picked : undefined;
}

/** What a report page needs of its project list: `offered`, the projects in the
    tab's scope (every project when `keys` is empty); `chosen`, the ones the
    URL's `project` params name among `offered`, falling back to `offered`; and
    `narrowed`, `chosen` when it is fewer than every project in `projects`, else
    `undefined`. */
export function reportProjects(
  params: URLSearchParams,
  projects: Project[],
  keys: string[],
): { offered: Project[]; chosen: Project[]; narrowed: Project[] | undefined } {
  const offered = projectsInScope(projects, keys);
  const chosen = resolveProjects(params, offered);

  return { offered, chosen, narrowed: narrowedTo(chosen, projects) };
}

/** A window as two epoch-millisecond instants. */
export interface Scale {
  from: number;
  to: number;
}

export const makeScale = (from: string, to: string): Scale => ({
  from: Date.parse(from),
  to: Date.parse(to),
});

/** Where an instant sits on the pane, `0..CHART_UNITS`, clamped: a segment that
    began before the window is drawn from its left edge, and one that has not
    ended (a `to` of null) is the caller's `CHART_UNITS`. */
export function xOf(scale: Scale, instant: string | number): number {
  const t = typeof instant === 'number' ? instant : Date.parse(instant);
  const x = ((t - scale.from) / (scale.to - scale.from)) * CHART_UNITS;

  return Math.min(CHART_UNITS, Math.max(0, x));
}

export type TickUnit = 'tenMinutes' | 'halfHour' | 'hour' | 'threeHours' | 'day' | 'week' | 'month';

const HOUR_MS = 3_600_000;

/** The step of every unit finer than a day, in milliseconds. */
const STEP_MS: Partial<Record<TickUnit, number>> = {
  tenMinutes: 10 * MINUTE_MS,
  halfHour: 30 * MINUTE_MS,
  hour: HOUR_MS,
  threeHours: 3 * HOUR_MS,
};

/** How coarse the ticks are: ten minutes each up to an hour wide, half an hour
    up to six hours, an hour up to twelve, three hours up to two days, a day each
    up to a month, a week each up to half a year, a month each beyond. */
export function tickUnit(scale: Scale): TickUnit {
  const width = scale.to - scale.from;
  const days = width / DAY_MS;

  if (width <= HOUR_MS) return 'tenMinutes';
  if (width <= 6 * HOUR_MS) return 'halfHour';
  if (width <= 12 * HOUR_MS) return 'hour';
  if (days <= 2) return 'threeHours';
  if (days <= 31) return 'day';
  if (days <= 183) return 'week';

  return 'month';
}

export interface Tick {
  x: number;
  label: string;
  unit: TickUnit;
}

/** The shifted instants of every boundary of `unit` at or after `start`, up to
    and including `end`. All in shifted time, where `getUTC*` is the reader's
    calendar. */
function boundaries(unit: TickUnit, start: number, end: number): number[] {
  const out: number[] = [];

  if (unit === 'month') {
    const first = new Date(start);
    let at = Date.UTC(first.getUTCFullYear(), first.getUTCMonth(), 1);
    if (at < start) at = Date.UTC(first.getUTCFullYear(), first.getUTCMonth() + 1, 1);

    while (at <= end) {
      out.push(at);
      const next = new Date(at);
      at = Date.UTC(next.getUTCFullYear(), next.getUTCMonth() + 1, 1);
    }

    return out;
  }

  const fine = STEP_MS[unit];
  if (fine) {
    const step = fine;
    let at = Math.ceil(start / step) * step;
    for (; at <= end; at += step) out.push(at);

    return out;
  }

  let at = Math.ceil(start / DAY_MS) * DAY_MS;
  if (unit === 'week') {
    while (new Date(at).getUTCDay() !== 1) at += DAY_MS;
  }

  const step = unit === 'week' ? 7 * DAY_MS : DAY_MS;
  for (; at <= end; at += step) out.push(at);

  return out;
}

/**
 * The tick marks, each at its `x` with its label.
 *
 * Only boundaries inside `[from, to]` are returned, both ends included. Labels:
 *
 * - `tenMinutes`, `halfHour`, `hour` and `threeHours` - `15:00`, with the short month and
 *   day beside it on the first tick
 *   and on every midnight, so the eye always has a date to hang the times on;
 * - `day` - the day number, with the short month on the first tick and on every
 *   1st, so the eye always has a month to hang the numbers on;
 * - `week` - `3 Oct`, since a Monday alone is not a date;
 * - `month` - the short month, with the year on every January and on the first
 *   tick.
 *
 * The optional locale exists so the tests can pin one; the page passes none and
 * gets the reader's.
 */
export function ticks(scale: Scale, offsetMinutes: number, locale?: string): Tick[] {
  if (!(scale.to > scale.from)) return [];

  const unit = tickUnit(scale);
  const shift = offsetMinutes * MINUTE_MS;
  const format = (shifted: number, options: Intl.DateTimeFormatOptions) =>
    new Date(shifted).toLocaleDateString(locale, { ...options, timeZone: 'UTC' });
  const formatTime = (shifted: number, options: Intl.DateTimeFormatOptions) =>
    new Date(shifted).toLocaleTimeString(locale, { ...options, timeZone: 'UTC' });

  return boundaries(unit, scale.from + shift, scale.to + shift).map((shifted, i) => {
    const first = i === 0;
    const date = new Date(shifted);
    let label: string;

    if (STEP_MS[unit]) {
      const time = formatTime(shifted, { hour: '2-digit', minute: '2-digit' });
      label =
        first || (date.getUTCHours() === 0 && date.getUTCMinutes() === 0)
          ? `${format(shifted, { day: 'numeric', month: 'short' })} ${time}`
          : time;
    } else if (unit === 'day') {
      label =
        first || date.getUTCDate() === 1
          ? format(shifted, { day: 'numeric', month: 'short' })
          : format(shifted, { day: 'numeric' });
    } else if (unit === 'week') {
      label = format(shifted, { day: 'numeric', month: 'short' });
    } else {
      label =
        first || date.getUTCMonth() === 0
          ? format(shifted, { month: 'short', year: 'numeric' })
          : format(shifted, { month: 'short' });
    }

    return { x: xOf(scale, shifted - shift), label, unit };
  });
}

/** Where now sits, or null when now is outside the window: `from` inclusive and
    `to` exclusive, so a window ending at the next midnight shows the marker
    right up to it and no marker is drawn on the edge of a finished window. */
export function todayX(scale: Scale, now: Date): number | null {
  const t = now.getTime();

  return t >= scale.from && t < scale.to ? xOf(scale, t) : null;
}

/** The `issue.key` of the synthetic entry the server answers for work under no
    epic. A real key is `HA-12`, so it cannot collide. */
export const OTHER_KEY = 'other';

/** One line of the chart: an entry, how deep it sits, and whether it can open. */
export interface TimelineRow {
  entry: TimelineEntry;
  depth: number;
  /** It has children in the answer to open. Not `isLeaf`: a story whose
      children were all inactive has none here and must not show a toggle. */
  expandable: boolean;
  /** Expandable and in the set, so its children follow it. */
  expanded: boolean;
  isOther: boolean;
}

/**
 * The answer as rows, depth first: each epic in the order answered, then Other
 * when there is one, and an entry's children straight after it - at one more
 * depth, recursively - only when its key is in `expanded`. Everything is
 * collapsed when the set is empty. A top-level entry whose key is in `hidden`
 * is skipped, and its children with it; a hidden key naming no entry changes
 * nothing, and `timeline` is only read. A row carries its entry untouched, so
 * its `segments` are its own and never a merge of its children's.
 */
export function foldRows(
  timeline: Timeline,
  expanded: ReadonlySet<string>,
  hidden: ReadonlySet<string> = new Set(),
): TimelineRow[] {
  const rows: TimelineRow[] = [];

  const visit = (entry: TimelineEntry, depth: number) => {
    const key = entry.issue.key;
    const expandable = entry.children.length > 0;
    const open = expandable && expanded.has(key);

    rows.push({ entry, depth, expandable, expanded: open, isOther: key === OTHER_KEY });
    if (open) for (const child of entry.children) visit(child, depth + 1);
  };

  for (const epic of timeline.epics) if (!hidden.has(epic.issue.key)) visit(epic, 0);
  if (timeline.other && !hidden.has(timeline.other.issue.key)) visit(timeline.other, 0);

  return rows;
}

/** The set with `key` added if it was absent, removed if it was there. A new
    set; the one passed in is never touched. */
export function toggled(expanded: ReadonlySet<string>, key: string): Set<string> {
  const next = new Set(expanded);
  if (!next.delete(key)) next.add(key);

  return next;
}

/** What a segment says on hover: `In Progress, 3 Oct to 9 Oct`, or `In
    Progress, 3 Oct onward` while it has not ended. Days are in the reader's
    own zone, which is right for a title and not for a tick. */
export function segmentTitle(statusName: string, segment: TimelineSegment, locale?: string): string {
  const from = dayLabel(segment.from, locale);
  const end = segment.to === null ? 'onward' : `to ${dayLabel(segment.to, locale)}`;

  return `${statusName}, ${from} ${end}`;
}

/** The custom properties a segment is painted with - its status's colour, or
    the default grey for a column that is not in `statuses` - and null on the
    Other row, which CSS paints neutrally. */
export function segmentStyle(
  statuses: Status[],
  segment: TimelineSegment,
  isOther: boolean,
): Record<string, string> | null {
  if (isOther) return null;

  return statusVars(statuses.find((status) => status.id === segment.statusId)?.color);
}

/** The footnote for stretches the server could not place, or null when there
    are none. */
export function unresolvedPhrase(count: number): string | null {
  if (count <= 0) return null;

  const what = count === 1 ? '1 column change' : `${count} column changes`;

  return `${what} could not be placed, because the column has since been renamed.`;
}
