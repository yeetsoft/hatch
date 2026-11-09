/* The bulk edit form, and the request it becomes.

   Every field on the form has three possible meanings and the API reads two of
   them from one JSON value: absent is "leave this alone", the empty string is
   "clear this", and anything else is the new value (IssuePatchRequest in
   Dtos.cs). A form control has no absent - a <select> always has a value - so
   the third state is spelled here, once, rather than being reinvented per
   field.

   This is the module the whole page hangs on, and it is separate from the page
   for one reason: getting it slightly wrong is silent. A form that sends
   `parentKey: ''` where it meant to send nothing does not fail - it quietly
   outdents fifty issues. */

import type { IssueBulkEditRequest, IssueBulkRekeyed, IssueBulkResult, IssueSearch, IssueType } from '../types';

/** The value a picker carries when it is not being used. Never a real key: no issue key is empty and none is this. */
export const KEEP = '__keep__';

export interface BulkForm {
  /** '' leaves the type alone. */
  type: IssueType | '';
  /** '' leaves the column alone. */
  statusId: number | '';
  /** KEEP leaves the parent alone, '' clears it, anything else is the new parent's key. */
  parent: string;
  /** KEEP leaves the project alone, otherwise the new project's id. */
  projectId: number | typeof KEEP;
  /** Only read when projectId !== KEEP. Matches useMoveProject's own default. */
  moveDescendants: boolean;
  /** Whether the ready date is being touched at all. */
  setReady: boolean;
  /** The wire form, or '' for the clear. Only read when setReady. */
  readyAt: string;
  setDue: boolean;
  dueAt: string;
}

export const EMPTY_FORM: BulkForm = {
  type: '',
  statusId: '',
  parent: KEEP,
  projectId: KEEP,
  moveDescendants: true,
  setReady: false,
  readyAt: '',
  setDue: false,
  dueAt: '',
};

/** Whether the form would change anything, which is what enables the button. */
export const isEmptyForm = (form: BulkForm): boolean =>
  form.type === '' &&
  form.statusId === '' &&
  form.parent === KEEP &&
  form.projectId === KEEP &&
  !form.setReady &&
  !form.setDue;

/**
 * The request, or null when there is nothing to send - no issues chosen, or a
 * form that names no change. Null rather than an empty request: the API refuses
 * both, and a refusal the browser could have avoided is a round trip spent
 * telling somebody what they already knew.
 */
export function buildBulkEdit(keys: string[], form: BulkForm): IssueBulkEditRequest | null {
  if (keys.length === 0 || isEmptyForm(form)) return null;

  const request: IssueBulkEditRequest = { keys };

  if (form.type !== '') request.type = form.type;
  if (form.statusId !== '') request.statusId = form.statusId;
  if (form.parent !== KEEP) request.parentKey = form.parent;
  if (form.projectId !== KEEP) {
    request.projectId = form.projectId;
    request.moveDescendants = form.moveDescendants;
  }
  if (form.setReady) request.readyAt = form.readyAt;
  if (form.setDue) request.dueAt = form.dueAt;

  return request;
}

/** The filter, before it is turned into a query. '' is "any", which is not a value the API takes. */
export interface Filters {
  projectId: number | '';
  type: IssueType | '';
  statusId: number | '';
  parentKey: string;
  ancestorKey: string;
  text: string;
}

export const NO_FILTERS: Filters = { projectId: '', type: '', statusId: '', parentKey: KEEP, ancestorKey: '', text: '' };

/**
 * The search the filter becomes, kept apart from the page so the scope rule is
 * testable. With no project chosen the search covers every project in the
 * scope, and a project chosen from a scope that has since narrowed is ignored.
 * Unscoped (`scopeIds` empty) the Project filter lists every project, so any
 * choice stands and no choice leaves the field out of the request.
 */
export function bulkSearch(filters: Filters, scopeIds: number[]): IssueSearch {
  const chosen = filters.projectId;
  const valid = chosen !== '' && (scopeIds.length === 0 || scopeIds.includes(chosen));
  const projectId = valid ? [chosen] : scopeIds.length > 0 ? scopeIds : null;

  return {
    projectId,
    type: filters.type === '' ? null : filters.type,
    statusId: filters.statusId === '' ? null : filters.statusId,
    // KEEP is "any parent"; the empty string is a filter of its own -
    // "no parent at all" - and has to reach the API as one.
    parentKey: filters.parentKey === KEEP ? null : filters.parentKey,
    ancestorKey: filters.ancestorKey || null,
    text: filters.text.trim() || null,
  };
}

/**
 * What happened, in one line. Says the boring parts out loud on purpose:
 * "already like that" is the answer to "why did nothing happen", and it is the
 * ordinary result of pressing Apply twice.
 */
export function summarize(result: IssueBulkResult): string {
  const parts = [`${result.changed.length} changed`];
  if (result.unchanged.length > 0) parts.push(`${result.unchanged.length} already like that`);
  if (result.failures.length > 0) parts.push(`${result.failures.length} refused`);
  if (result.rekeyed.length > 0) {
    const to = result.rekeyed[0].to;
    parts.push(`${result.rekeyed.length} moved to ${to.slice(0, to.lastIndexOf('-'))}`);
  }
  return parts.join(', ');
}

/**
 * The new keys of the issues an edit moved out of the tab's scope. A moved issue
 * drops off the results table, so without this the only trace of it is a count.
 */
export function movedOutOfScope(rekeyed: IssueBulkRekeyed[], inScope: (projectKey: string) => boolean): string[] {
  return rekeyed.filter((pair) => !inScope(pair.to.slice(0, pair.to.lastIndexOf('-')))).map((pair) => pair.to);
}
