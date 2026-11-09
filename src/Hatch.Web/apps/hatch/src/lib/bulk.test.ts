import { describe, expect, it } from 'vitest';
import { EMPTY_FORM, KEEP, NO_FILTERS, buildBulkEdit, bulkSearch, isEmptyForm, movedOutOfScope, summarize } from './bulk';
import type { BulkForm, Filters } from './bulk';

const form = (over: Partial<BulkForm> = {}): BulkForm => ({ ...EMPTY_FORM, ...over });
const filters = (over: Partial<Filters> = {}): Filters => ({ ...NO_FILTERS, ...over });
const KEYS = ['AER-1', 'AER-2'];
const SCOPE = [1, 2];

describe('buildBulkEdit', () => {
  it('sends nothing when nothing was chosen', () => {
    expect(buildBulkEdit([], form({ statusId: 3 }))).toBeNull();
    expect(buildBulkEdit(KEYS, EMPTY_FORM)).toBeNull();
  });

  /* The property the whole module exists for. A field left alone must not
     appear in the body at all - an empty string there is the clear, and a form
     that sends one it did not mean outdents fifty issues without erroring. */
  it('names only the fields that were touched', () => {
    expect(buildBulkEdit(KEYS, form({ statusId: 3 }))).toEqual({ keys: KEYS, statusId: 3 });
  });

  it('carries a new type and a new column together', () => {
    expect(buildBulkEdit(KEYS, form({ type: 'bug', statusId: 3 }))).toEqual({
      keys: KEYS,
      type: 'bug',
      statusId: 3,
    });
  });

  it('sends a new parent as its key', () => {
    expect(buildBulkEdit(KEYS, form({ parent: 'AER-9' }))?.parentKey).toBe('AER-9');
  });

  it('sends the empty string when the parent is being cleared', () => {
    expect(buildBulkEdit(KEYS, form({ parent: '' }))?.parentKey).toBe('');
  });

  it('leaves parentKey off entirely when the parent is being left alone', () => {
    const request = buildBulkEdit(KEYS, form({ parent: KEEP, statusId: 3 }))!;
    expect('parentKey' in request).toBe(false);
  });

  it('sends a date only when its box is ticked, and the empty string clears it', () => {
    expect(buildBulkEdit(KEYS, form({ setDue: true, dueAt: '2027-09-01' }))?.dueAt).toBe('2027-09-01');
    expect(buildBulkEdit(KEYS, form({ setDue: true, dueAt: '' }))?.dueAt).toBe('');
  });

  /* A date typed and then unticked is a date nobody meant to send. The value is
     kept in the form so unticking is undoable, and dropped here. */
  it('ignores a date that was typed and then unticked', () => {
    const request = buildBulkEdit(KEYS, form({ setDue: false, dueAt: '2027-09-01', statusId: 3 }))!;
    expect('dueAt' in request).toBe(false);
  });

  it('carries both dates when both are being set', () => {
    const request = buildBulkEdit(KEYS, form({ setReady: true, readyAt: '2027-08-15', setDue: true, dueAt: '' }))!;
    expect(request.readyAt).toBe('2027-08-15');
    expect(request.dueAt).toBe('');
  });

  it('sends the project and moveDescendants together once a project is chosen', () => {
    expect(buildBulkEdit(KEYS, form({ projectId: 7, moveDescendants: true }))).toEqual({
      keys: KEYS,
      projectId: 7,
      moveDescendants: true,
    });
    expect(buildBulkEdit(KEYS, form({ projectId: 7, moveDescendants: false }))).toEqual({
      keys: KEYS,
      projectId: 7,
      moveDescendants: false,
    });
  });

  it('leaves projectId and moveDescendants off entirely when the project is being left alone', () => {
    const request = buildBulkEdit(KEYS, form({ projectId: KEEP, statusId: 3 }))!;
    expect('projectId' in request).toBe(false);
    expect('moveDescendants' in request).toBe(false);
  });
});

describe('isEmptyForm', () => {
  it('is true only for a form that would change nothing', () => {
    expect(isEmptyForm(EMPTY_FORM)).toBe(true);
    expect(isEmptyForm(form({ type: 'task' }))).toBe(false);
    expect(isEmptyForm(form({ parent: '' }))).toBe(false);
    expect(isEmptyForm(form({ setReady: true }))).toBe(false);
    expect(isEmptyForm(form({ projectId: 7 }))).toBe(false);
  });
});

describe('summarize', () => {
  it('says only what happened', () => {
    expect(summarize({ changed: ['AER-1'], unchanged: [], failures: [], rekeyed: [] })).toBe('1 changed');
  });

  it('says the boring parts too, because they are the answer to "why did nothing happen"', () => {
    expect(
      summarize({
        changed: [],
        unchanged: ['AER-1', 'AER-2'],
        failures: [{ key: 'AER-3', reason: 'a task hangs under a story' }],
        rekeyed: [],
      }),
    ).toBe('0 changed, 2 already like that, 1 refused');
  });

  it('says how many moved to the new project, when any did', () => {
    expect(
      summarize({
        changed: ['AER-1'],
        unchanged: [],
        failures: [],
        rekeyed: [{ from: 'AER-1', to: 'BER-5' }],
      }),
    ).toBe('1 changed, 1 moved to BER');
  });
});

describe('movedOutOfScope', () => {
  const inAer = (key: string): boolean => key === 'AER';

  it('names nothing when nothing was moved', () => {
    expect(movedOutOfScope([], inAer)).toEqual([]);
  });

  it('names only the keys that landed outside the scope', () => {
    const rekeyed = [
      { from: 'FIL-1', to: 'AER-7' },
      { from: 'AER-2', to: 'FIL-3' },
    ];
    expect(movedOutOfScope(rekeyed, inAer)).toEqual(['FIL-3']);
  });

  it('names nothing when everything is in scope', () => {
    expect(movedOutOfScope([{ from: 'AER-2', to: 'FIL-3' }], () => true)).toEqual([]);
  });
});

describe('bulkSearch', () => {
  it('searches every project in the scope when none is chosen', () => {
    expect(bulkSearch(filters(), SCOPE).projectId).toEqual([1, 2]);
  });

  it('leaves the project out of the request when unscoped and none is chosen', () => {
    expect(bulkSearch(filters(), []).projectId).toBeNull();
  });

  it('narrows to the chosen project when it is in the scope', () => {
    expect(bulkSearch(filters({ projectId: 2 }), SCOPE).projectId).toEqual([2]);
  });

  it('ignores a chosen project the scope has since left behind', () => {
    expect(bulkSearch(filters({ projectId: 3 }), SCOPE).projectId).toEqual([1, 2]);
  });

  it('lets any chosen project through when unscoped', () => {
    expect(bulkSearch(filters({ projectId: 3 }), []).projectId).toEqual([3]);
  });

  it('keeps "no parent at all" apart from "any parent"', () => {
    expect(bulkSearch(filters({ parentKey: '' }), SCOPE).parentKey).toBe('');
    expect(bulkSearch(filters({ parentKey: KEEP }), SCOPE).parentKey).toBeNull();
    expect(bulkSearch(filters({ parentKey: 'AER-9' }), SCOPE).parentKey).toBe('AER-9');
  });

  it('trims the text and drops it when nothing is left', () => {
    expect(bulkSearch(filters({ text: '  needle ' }), SCOPE).text).toBe('needle');
    expect(bulkSearch(filters({ text: '' }), SCOPE).text).toBeNull();
    expect(bulkSearch(filters({ text: '   ' }), SCOPE).text).toBeNull();
  });

  it('sends type, column and ancestor only when set', () => {
    const empty = bulkSearch(filters(), SCOPE);
    expect(empty.type).toBeNull();
    expect(empty.statusId).toBeNull();
    expect(empty.ancestorKey).toBeNull();

    const set = bulkSearch(filters({ type: 'bug', statusId: 3, ancestorKey: 'AER-1' }), SCOPE);
    expect(set.type).toBe('bug');
    expect(set.statusId).toBe(3);
    expect(set.ancestorKey).toBe('AER-1');
  });
});
