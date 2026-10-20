/* Reading a playbook row: which move it speaks for. */

import { ISSUE_TYPES } from '../types';
import type { IssueType, Playbook, PlaybookFileDto } from '../types';

/**
 * A row whose two ends are the same column is the review playbook: what an
 * agent is told when a pull request in review has stopped merging cleanly or its
 * build has failed - one row answers both, and the runner brings the facts.
 *
 * The server only accepts that for the review column, and says which column may
 * when it is tried on any other - so nothing here checks which column it is,
 * and a row that reached the page with both ends equal is one the server let
 * through.
 */
export const isReviewPlaybook = (p: Pick<Playbook, 'fromStatusId' | 'toStatusId'>): boolean =>
  p.fromStatusId === p.toStatusId;

/**
 * The move a row is for, as a phrase: `todo to in progress`, or `review
 * review` for the review playbook. What a control's accessible name is
 * built from, so two rows on one page never share one.
 */
export function transitionLabel(p: Pick<Playbook, 'fromStatusId' | 'toStatusId' | 'fromStatusName' | 'toStatusName'>): string {
  return isReviewPlaybook(p) ? `${p.fromStatusName} review` : `${p.fromStatusName} to ${p.toStatusName}`;
}

/** What the budget field shows: blank for no cap, the number otherwise.
    Mirrors wipLimitDraft - see lib/wip.ts. */
export function budgetDraft(budget: number | null): string {
  return budget === null ? '' : String(budget);
}

/** The field, trimmed, as the wire's `budget` - blank clears it. */
export function budgetRequest(draft: string): string {
  return draft.trim();
}

/** What the brief limit field shows: blank for no cap, the number otherwise.
    Mirrors budgetDraft. */
export function briefLimitDraft(briefLimit: number | null): string {
  return briefLimit === null ? '' : String(briefLimit);
}

/** The field, trimmed, as the wire's `briefLimit` - blank clears it. */
export function briefLimitRequest(draft: string): string {
  return draft.trim();
}

/** What the denied tools field shows: one rule per line, blank for none. */
export function deniedToolsDraft(rules: string[]): string {
  return rules.join('\n');
}

/** The field, split into rules for the wire: one per line, trimmed, blanks dropped. */
export function deniedToolsRequest(draft: string): string[] {
  return draft
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l !== '');
}

/** What a row's project reads as: "All projects" for a board-wide row, "KEY — Name" otherwise. */
export function projectLabel(p: Pick<Playbook, 'projectKey' | 'projectName'>): string {
  return p.projectKey === null || p.projectName === null
    ? 'All projects'
    : `${p.projectKey} — ${p.projectName}`;
}

/** A row's label: board-wide, or the project's own - see HA-460. Drawn on
    every row of the main table, filtered or not. */
export function playbookLabel(p: Pick<Playbook, 'projectKey'>): string {
  return p.projectKey === null ? 'board-wide' : `${p.projectKey}'s own`;
}

/** A file row's label - the third of the three, drawn once a repo-sourced
    project's own file is merged into the table under its filter. */
export function fileRowLabel(file: Pick<PlaybookFileDto, 'trunk' | 'sha'>): string {
  return `.hatch/playbooks.yaml on ${file.trunk} at ${file.sha.slice(0, 7)}`;
}

/** What coverage reads of a row: its ends, the types it names, its shape.
    Both Playbook and PlaybookFileRowDto satisfy it. */
export type CoverageRow = Pick<Playbook, 'fromStatusId' | 'toStatusId' | 'types' | 'shape'>;

/** One kind of issue a playbook can be asked to speak for: a type, leaf or parent. */
export interface CoverageCell {
  type: IssueType;
  shape: 'leaf' | 'parent';
}

/** How much of a board-wide row a project's own rows leave standing. */
export type Coverage = 'overridden' | 'partly' | 'live';

const cellKey = (c: CoverageCell): string => `${c.type}:${c.shape}`;

/**
 * The cells a row covers: its types (every type when none are listed) crossed
 * with its shape (`any` covers leaf and parent). Types come out in ISSUE_TYPES
 * order, whatever order the row lists them in.
 *
 * The three functions here mirror the dispatcher, not each other's idea of a
 * match: `Dispatch.Match` keeps the candidates on the exact (from, to) that
 * `EfHatchPlaybook.Covers` the issue, own layer first - see docs/hatch.md,
 * "Playbooks". Between layers only coverage counts: specificity, age, prompt and
 * model play no part. The review playbook (equal ends) is an ordinary
 * transition and gets no special case.
 */
export function coverageCells(row: CoverageRow): CoverageCell[] {
  const types = row.types.length === 0 ? ISSUE_TYPES : ISSUE_TYPES.filter((t) => row.types.includes(t));
  const shapes: CoverageCell['shape'][] = row.shape === 'any' ? ['leaf', 'parent'] : [row.shape];
  return types.flatMap((type) => shapes.map((shape) => ({ type, shape })));
}

/** The board-wide row's cells that none of the project's own rows on the same
    transition cover - the moves it would still answer for that project. */
export function cellsLeft(boardRow: CoverageRow, ownRows: CoverageRow[]): CoverageCell[] {
  const taken = new Set(
    ownRows
      .filter((r) => r.fromStatusId === boardRow.fromStatusId && r.toStatusId === boardRow.toStatusId)
      .flatMap(coverageCells)
      .map(cellKey),
  );
  return coverageCells(boardRow).filter((c) => !taken.has(cellKey(c)));
}

/** Nothing left is `overridden`, all of it left is `live`, anything between is `partly`. */
export function coverageOf(boardRow: CoverageRow, ownRows: CoverageRow[]): Coverage {
  const left = cellsLeft(boardRow, ownRows).length;
  if (left === 0) return 'overridden';
  return left === coverageCells(boardRow).length ? 'live' : 'partly';
}

const TYPE_PLURALS: Record<IssueType, string> = {
  epic: 'epics',
  reflection: 'reflections',
  story: 'stories',
  task: 'tasks',
  bug: 'bugs',
};

const andList = (xs: string[]): string =>
  xs.length < 2 ? xs.join('') : `${xs.slice(0, -1).join(', ')} and ${xs[xs.length - 1]}`;

/**
 * What a partly overridden board-wide row still runs for, in words - what the
 * dispatcher (`Dispatch.Match`) would still send to the row, read off the cells
 * `cellsLeft` returns. `for tasks and bugs only` when the leftover is a
 * rectangle narrower in type, `for parents only` / `for leaves only` when
 * narrower in shape, both at once as `for parent tasks and bugs only`; a
 * leftover that is no rectangle names each remaining shape's types in turn.
 * Types come out in ISSUE_TYPES order, whatever order `left` is in.
 *
 * `null` when the row is live or overridden: there is nothing to add.
 */
export function leftNote(boardRow: CoverageRow, left: CoverageCell[]): string | null {
  const all = coverageCells(boardRow);
  if (left.length === 0 || left.length === all.length) return null;

  const shapes: CoverageCell['shape'][] = ['leaf', 'parent'];
  const typesOf = (cells: CoverageCell[], shape?: CoverageCell['shape']): IssueType[] =>
    ISSUE_TYPES.filter((t) => cells.some((c) => c.type === t && (shape === undefined || c.shape === shape)));
  const shapesOf = (cells: CoverageCell[]): CoverageCell['shape'][] =>
    shapes.filter((s) => cells.some((c) => c.shape === s));

  const leftTypes = typesOf(left);
  const leftShapes = shapesOf(left);
  if (leftTypes.length * leftShapes.length !== left.length) {
    const clauses = leftShapes.map((s) => `${s} ${andList(typesOf(left, s).map((t) => TYPE_PLURALS[t]))}`);
    return `for ${clauses.join('; ')}`;
  }

  const typesNarrowed = leftTypes.length < typesOf(all).length;
  const shapesNarrowed = leftShapes.length < shapesOf(all).length;
  const nouns = andList(leftTypes.map((t) => TYPE_PLURALS[t]));
  if (typesNarrowed && shapesNarrowed) return `for ${leftShapes[0]} ${nouns} only`;
  if (typesNarrowed) return `for ${nouns} only`;
  return `for ${leftShapes[0] === 'leaf' ? 'leaves' : 'parents'} only`;
}

/** What a board-wide row's note reads under the page's project filter: `overridden
    by ${ownLabel}` once nothing of it is left, leftNote's own words for the part
    that is, null for a row still fully live. `ownLabel` is the filtered project's
    playbookLabel (`<KEY>'s own`) for a table project, or the literal `the file`
    for a repo-sourced one - "overridden" is the one word used either way. */
export function boardRowNote(boardRow: CoverageRow, ownRows: CoverageRow[], ownLabel: string): string | null {
  const left = cellsLeft(boardRow, ownRows);
  if (left.length === 0) return `overridden by ${ownLabel}`;
  return leftNote(boardRow, left);
}

/** What a repo-sourced project's own leftover table row's note reads -
    unconditional, since Dispatch.MatchAsync (src/Hatch.Api/Modules/Hatch/Dispatch.cs:1361)
    excludes every row naming a project from the query itself once that project
    is repo-sourced, whatever the row would otherwise cover. */
export function notReadNote(projectKey: string): string {
  return `not read while ${projectKey} reads its file`;
}

/** Whether the page's project filter keeps this row: every row when cleared, every
    board-wide row regardless, otherwise only a row naming that project. */
export function matchesProjectFilter(p: Pick<Playbook, 'projectKey'>, projectKey: string): boolean {
  return projectKey === '' || p.projectKey === null || p.projectKey === projectKey;
}

/** A check that can edit - the case the epic exists to prevent. */
export function isUncheckedEdit(p: Pick<Playbook, 'isCheck' | 'deniedTools'>): boolean {
  return p.isCheck && p.deniedTools.length === 0;
}

/** Whether this row's prompt is the operator's own wording rather than its prescribed
    text - what the badge marks, and the words HA-904's diff panel must reuse. A row with
    no prescribed text is never marked. */
export function isOverridden(p: Pick<Playbook, 'prompt' | 'defaultPrompt'>): boolean {
  return p.defaultPrompt !== null && p.prompt !== p.defaultPrompt;
}
