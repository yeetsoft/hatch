import { describe, expect, it } from 'vitest';
import { lookupView, mergePickerRows, pickerNote, rowOf, settleHighlight } from './pickerLookup';
import type { PickedRow, PickerLookupFinished } from './pickerLookup';
import type { GoToRowDto, IssueCard } from '../types';

/* Only what rowOf reads; the rest of a card is not its business. */
const card = (key: string, title: string, type = 'story'): IssueCard =>
  ({ key, projectKey: key.slice(0, key.lastIndexOf('-')), type, title, statusId: 1 }) as IssueCard;

const row = (key: string, over: Partial<GoToRowDto> = {}): GoToRowDto => ({
  key,
  projectKey: key.slice(0, key.lastIndexOf('-')),
  number: Number(key.slice(key.lastIndexOf('-') + 1)),
  type: 'task',
  title: `Title of ${key}`,
  outOfScope: false,
  statusId: 2,
  statusName: 'Done',
  statusColor: '#336699',
  ...over,
});

const picked = (key: string, title = `Title of ${key}`, type = 'task'): PickedRow => ({ key, title, type });

describe('rowOf', () => {
  it('takes the key, the title and the type from a card', () => {
    expect(rowOf(card('AER-1', 'A title'))).toEqual({ key: 'AER-1', title: 'A title', type: 'story' });
  });

  it('takes them from a looked-up row the same way', () => {
    expect(rowOf(row('AER-2', { title: 'Another' }))).toEqual({ key: 'AER-2', title: 'Another', type: 'task' });
  });
});

describe('mergePickerRows', () => {
  const accept = () => true;

  it('lists the page rows first, then the server rows, in their own orders', () => {
    const page = [picked('AER-5'), picked('AER-1')];
    const looked = [row('AER-9'), row('AER-3')];

    expect(mergePickerRows(page, looked, accept)).toEqual([
      picked('AER-5'),
      picked('AER-1'),
      picked('AER-9'),
      picked('AER-3'),
    ]);
  });

  it('drops a looked-up row outside the scope', () => {
    const looked = [row('AER-9', { outOfScope: true })];

    expect(mergePickerRows([], looked, accept)).toEqual([]);
  });

  it('drops a looked-up row accept refuses', () => {
    const looked = [row('AER-9')];

    expect(mergePickerRows([], looked, () => false)).toEqual([]);
  });

  it('drops a looked-up row whose key is already among the page rows', () => {
    const page = [picked('AER-9', 'The page’s own title')];
    const looked = [row('AER-9', { title: 'The server’s title' })];

    expect(mergePickerRows(page, looked, accept)).toEqual([page[0]]);
  });

  it('lists a looked-up row the typed text never matched, since the server matched it its own way', () => {
    // The candidate it came from never went through pickerRows at all - it is
    // not in `page` - so nothing here re-checks the query against it.
    const looked = [row('AER-9', { title: 'Renaming the booth' })];

    expect(mergePickerRows([], looked, accept)).toEqual([picked('AER-9', 'Renaming the booth')]);
  });
});

describe('lookupView', () => {
  it('asks nothing of an empty query', () => {
    expect(lookupView('', null)).toEqual({ rows: [], looking: false });
    expect(lookupView('   ', { query: '', rows: [row('AER-1')], failed: false })).toEqual({
      rows: [],
      looking: false,
    });
  });

  it('is still looking before any answer has come back', () => {
    expect(lookupView('ha', null)).toEqual({ rows: [], looking: true });
  });

  it('is still looking while the last answer was to older text', () => {
    const finished: PickerLookupFinished = { query: 'h', rows: [row('AER-1')], failed: false };

    expect(lookupView('ha', finished)).toEqual({ rows: [], looking: true });
  });

  it('draws the finished answer once it matches what is typed, trimmed', () => {
    const finished: PickerLookupFinished = { query: 'ha', rows: [row('AER-1'), row('AER-2')], failed: false };

    expect(lookupView('  ha  ', finished)).toEqual({ rows: finished.rows, looking: false });
  });

  it('draws nothing, and is not looking, once a finished answer to this text failed', () => {
    const finished: PickerLookupFinished = { query: 'ha', rows: [], failed: true };

    expect(lookupView('ha', finished)).toEqual({ rows: [], looking: false });
  });
});

describe('pickerNote', () => {
  it('without a lookup: empty when there are no candidates at all', () => {
    expect(pickerNote({ lookup: false, candidates: 0, rows: 0, typed: '', looking: false })).toBe('empty');
    expect(pickerNote({ lookup: false, candidates: 0, rows: 0, typed: 'x', looking: false })).toBe('empty');
  });

  it('without a lookup: nothing once there are candidates but none left to draw', () => {
    expect(pickerNote({ lookup: false, candidates: 3, rows: 0, typed: 'x', looking: false })).toBe('nothing');
  });

  it('without a lookup: null once a row is left to draw', () => {
    expect(pickerNote({ lookup: false, candidates: 3, rows: 1, typed: 'x', looking: false })).toBe(null);
  });

  it('with a lookup: looking wins over every other note', () => {
    expect(pickerNote({ lookup: true, candidates: 0, rows: 0, typed: '', looking: true })).toBe('looking');
    expect(pickerNote({ lookup: true, candidates: 5, rows: 5, typed: 'x', looking: true })).toBe('looking');
  });

  it('with a lookup: empty only once nothing is typed and the page holds no candidates', () => {
    expect(pickerNote({ lookup: true, candidates: 0, rows: 0, typed: '', looking: false })).toBe('empty');
    expect(pickerNote({ lookup: true, candidates: 0, rows: 2, typed: 'x', looking: false })).toBe(null);
  });

  it('with a lookup: nothing typed and candidates on the page draws the list, not empty', () => {
    expect(pickerNote({ lookup: true, candidates: 3, rows: 3, typed: '', looking: false })).toBe(null);
  });

  it('with a lookup: nothing once something is typed, nothing is out, and no row is left', () => {
    expect(pickerNote({ lookup: true, candidates: 3, rows: 0, typed: 'x', looking: false })).toBe('nothing');
  });

  it('with a lookup: null once a row is left to draw', () => {
    expect(pickerNote({ lookup: true, candidates: 0, rows: 1, typed: 'x', looking: false })).toBe(null);
  });
});

describe('settleHighlight', () => {
  it('moves a null highlight onto the first row past the clear row once one exists', () => {
    expect(settleHighlight(null, 2, 1)).toBe(1);
  });

  it('leaves a null highlight null when nothing is past the clear row', () => {
    expect(settleHighlight(null, 1, 1)).toBe(null);
    expect(settleHighlight(null, 0, 0)).toBe(null);
  });

  it('leaves an index on a row the list still reaches untouched', () => {
    expect(settleHighlight(1, 5, 1)).toBe(1);
  });

  it('clamps an index one past the end onto the last row', () => {
    expect(settleHighlight(4, 3, 1)).toBe(2);
  });

  it('falls back to null when the list has emptied out from under it', () => {
    expect(settleHighlight(0, 0, 0)).toBe(null);
  });
});
