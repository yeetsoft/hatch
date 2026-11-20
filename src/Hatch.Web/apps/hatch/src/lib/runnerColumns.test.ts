import { describe, expect, it } from 'vitest';
import {
  allColumns,
  columnLit,
  columnsHint,
  columnsWords,
  noColumns,
  onlyColumn,
  sameColumnIds,
  toggleColumn,
  workableColumns,
} from './runnerColumns';
import type { Status } from '../types';

const status = (id: number, name: string, sortOrder: number, over: Partial<Status> = {}): Status => ({
  id,
  name,
  sortOrder,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: [],
  color: '#6b7280',
  ...over,
});

// Deliberately out of order, with a terminal and a deferred column among them.
const board = (): Status[] => [
  status(4, 'In Review', 40),
  status(9, 'Done', 90, { isTerminal: true }),
  status(2, 'Breakdown', 20),
  status(7, 'Later', 70, { isDeferred: true }),
  status(3, 'In Progress', 30),
  status(5, 'Testing', 50),
];

describe('workableColumns', () => {
  it('drops terminal and deferred columns and orders by sortOrder', () => {
    expect(workableColumns(board()).map((c) => c.name)).toEqual(['Breakdown', 'In Progress', 'In Review', 'Testing']);
  });

  it('leaves the list it was given alone', () => {
    const statuses = board();
    workableColumns(statuses);
    expect(statuses.map((s) => s.id)).toEqual([4, 9, 2, 7, 3, 5]);
  });
});

describe('columnLit', () => {
  it('lights everything under all, ticked or not', () => {
    expect(columnLit('all', true)).toBe(true);
    expect(columnLit('all', false)).toBe(true);
  });

  it('dims a ticked column under except and lights an unticked one', () => {
    expect(columnLit('except', true)).toBe(false);
    expect(columnLit('except', false)).toBe(true);
  });

  it('lights a ticked column under only and dims an unticked one', () => {
    expect(columnLit('only', true)).toBe(true);
    expect(columnLit('only', false)).toBe(false);
  });
});

describe('the presses', () => {
  it('All ticks every listed column, and None clears them', () => {
    expect(allColumns(workableColumns(board()))).toEqual([2, 3, 4, 5]);
    expect(noColumns()).toEqual([]);
  });

  it('only ticks one column alone', () => {
    expect(onlyColumn(2)).toEqual([2]);
  });

  it('toggles a column on and off without touching the others', () => {
    expect(toggleColumn([2], 3)).toEqual([2, 3]);
    expect(toggleColumn([2, 3], 2)).toEqual([3]);
  });

  it('compares id lists as sets', () => {
    expect(sameColumnIds([2, 3], [3, 2])).toBe(true);
    expect(sameColumnIds([2], [2, 3])).toBe(false);
    expect(sameColumnIds([], [])).toBe(true);
  });
});

describe('columnsHint', () => {
  it('says what nothing ticked means, and nothing under all', () => {
    expect(columnsHint('only')).toContain('Nothing ticked works nothing.');
    expect(columnsHint('except')).toContain('Nothing ticked works everything.');
    expect(columnsHint('all')).toBe('');
  });
});

describe('columnsWords', () => {
  it('reads every column', () => {
    expect(columnsWords('all', [], board())).toBe('every column');
    expect(columnsWords('all', [3], board())).toBe('every column');
  });

  it('names the skipped columns in board order under except', () => {
    expect(columnsWords('except', [5, 3], board())).toBe('every column except In Progress, Testing');
  });

  it('names the worked columns in board order under only', () => {
    expect(columnsWords('only', [4, 2], board())).toBe('only Breakdown, In Review');
  });

  it('reads an empty only as no column, and an empty except as every column', () => {
    expect(columnsWords('only', [], board())).toBe('no column');
    expect(columnsWords('except', [], board())).toBe('every column');
  });

  it('skips an id the list does not draw', () => {
    expect(columnsWords('only', [9, 7, 2], board())).toBe('only Breakdown');
    expect(columnsWords('only', [9], board())).toBe('no column');
  });
});
