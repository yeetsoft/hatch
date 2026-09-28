import { describe, expect, it } from 'vitest';
import {
  canUndo,
  cascadeClause,
  cascadeEntries,
  cascadeOrder,
  dismiss,
  dropConfirmation,
  newestUndoable,
  raise,
  raiseMove,
  settle,
  undoneNote,
  withCascade,
} from './confirmations';
import type { CascadeEntry, Confirmation, MovedConfirmation, MoveRaise, MoveState } from './confirmations';
import type { CloseOffer } from './closeSubtree';
import type { Board, IssueCard, Status } from '../types';

const filed = (id: number, key: string): Confirmation => ({ id, kind: 'filed', issueKey: key, title: `title ${key}` });

describe('raise', () => {
  it('takes the key and the title off the issue the server returned', () => {
    expect(raise([], { key: 'AER-12', title: 'Drain retries' }, 1)).toEqual([
      { id: 1, kind: 'filed', issueKey: 'AER-12', title: 'Drain retries' },
    ]);
  });

  it('puts the newest on the front and leaves what was there', () => {
    const stack = raise([filed(1, 'AER-12')], { key: 'AER-13', title: 'title AER-13' }, 2);

    expect(stack.map((c) => c.issueKey)).toEqual(['AER-13', 'AER-12']);
  });

  /* Two filings are two chicklets. Nothing replaces anything, so a planning
     session that files six can still see all six. */
  it('never replaces a chicklet already on screen', () => {
    let stack: Confirmation[] = [];
    for (const key of ['AER-12', 'AER-13', 'AER-14']) {
      stack = raise(stack, { key, title: `title ${key}` }, stack.length + 1);
    }

    expect(stack).toHaveLength(3);
    expect(new Set(stack.map((c) => c.id)).size).toBe(3);
  });

  /* The id is what keeps them apart, not the key: the same issue confirmed
     twice is two rows that can be closed one at a time. */
  it('keeps two filings of the same key apart', () => {
    const stack = raise(raise([], { key: 'AER-12', title: 'Drain retries' }, 1), { key: 'AER-12', title: 'Drain retries' }, 2);

    expect(stack.map((c) => c.id)).toEqual([2, 1]);
  });

  it('does not touch the stack it was given', () => {
    const before = [filed(1, 'AER-12')];
    raise(before, { key: 'AER-13', title: 'title AER-13' }, 2);

    expect(before).toHaveLength(1);
  });
});

describe('dismiss', () => {
  it('takes exactly one off and leaves the rest in order', () => {
    const stack = [filed(3, 'AER-14'), filed(2, 'AER-13'), filed(1, 'AER-12')];

    expect(dismiss(stack, 2).map((c) => c.issueKey)).toEqual(['AER-14', 'AER-12']);
  });

  it('is a no-op for an id that is not in the stack', () => {
    const stack = [filed(2, 'AER-13'), filed(1, 'AER-12')];

    expect(dismiss(stack, 99)).toEqual(stack);
  });

  it('empties a stack of one', () => {
    expect(dismiss([filed(1, 'AER-12')], 1)).toEqual([]);
  });

  it('does not touch the stack it was given', () => {
    const before = [filed(1, 'AER-12')];
    dismiss(before, 1);

    expect(before).toHaveLength(1);
  });
});

const drop = (key: string): MoveRaise => ({
  issueKey: key,
  title: `title ${key}`,
  from: { id: 1, name: 'Backlog' },
  to: { id: 2, name: 'To Do' },
  restore: { statusId: 1, afterKey: null, beforeKey: 'AER-1', fromStatusId: 2 },
});

/** A stack built oldest first, the way the corner is raised. */
const stacked = (...states: (MoveState | 'filed')[]): Confirmation[] =>
  states.reduce<Confirmation[]>((stack, state, i) => {
    const id = i + 1;
    if (state === 'filed') return raise(stack, { key: `AER-${id}`, title: `title ${id}` }, id);
    return settle(raiseMove(stack, drop(`AER-${id}`), id), id, state, null);
  }, []);

describe('raiseMove', () => {
  it('puts the drop on the front, ready to be taken back', () => {
    const [c] = raiseMove([], drop('AER-12'), 7);

    expect(c).toEqual({ ...drop('AER-12'), id: 7, kind: 'moved', state: 'moved', note: null, cascade: [] });
  });

  it('stacks among the filings, newest first', () => {
    const stack = raiseMove(raise([], { key: 'AER-11', title: 'filed' }, 1), drop('AER-12'), 2);

    expect(stack.map((c) => [c.kind, c.issueKey])).toEqual([
      ['moved', 'AER-12'],
      ['filed', 'AER-11'],
    ]);
  });

  /* Dragging the same card back and forth is two drops, and each can be taken
     back on its own. */
  it('keeps two drops of one card apart', () => {
    const stack = raiseMove(raiseMove([], drop('AER-12'), 1), drop('AER-12'), 2);

    expect(stack.map((c) => c.id)).toEqual([2, 1]);
  });
});

describe('settle', () => {
  it('changes one chicklet and what it says, and nothing else', () => {
    const stack = stacked('moved', 'moved');

    const [newer, older] = settle(stack, 1, 'undone', 'moved back to Backlog');

    expect(older).toMatchObject({ id: 1, state: 'undone', note: 'moved back to Backlog' });
    expect(newer).toMatchObject({ id: 2, state: 'moved', note: null });
  });

  it('leaves a filed chicklet and an unknown id alone', () => {
    const stack = stacked('filed', 'moved');

    expect(settle(stack, 1, 'undone', 'x')).toEqual(stack);
    expect(settle(stack, 99, 'undone', 'x')).toEqual(stack);
  });

  it('does not touch the stack it was given', () => {
    const before = stacked('moved');
    settle(before, 1, 'undone', 'x');

    expect(before[0]).toMatchObject({ state: 'moved' });
  });
});

describe('canUndo', () => {
  it('is true for a move that stands and one whose undo failed', () => {
    expect(canUndo(stacked('moved')[0])).toBe(true);
    expect(canUndo(stacked('failed')[0])).toBe(true);
  });

  it('is false for one in flight, one over, and a filing', () => {
    for (const state of ['undoing', 'undone', 'refused'] as const) expect(canUndo(stacked(state)[0])).toBe(false);
    expect(canUndo(stacked('filed')[0])).toBe(false);
  });
});

describe('newestUndoable', () => {
  it('is the newest move, past any filing on top of it', () => {
    const stack = stacked('moved', 'moved', 'filed');

    expect(newestUndoable(stack)?.id).toBe(2);
  });

  /* Each press takes one step further back through the stack. */
  it('walks back as each is undone', () => {
    let stack = stacked('moved', 'moved', 'moved');
    const undone: number[] = [];

    for (let i = 0; i < 4; i++) {
      const next = newestUndoable(stack);
      if (!next) break;
      undone.push(next.id);
      stack = settle(stack, next.id, 'undone', 'moved back');
    }

    expect(undone).toEqual([3, 2, 1]);
    expect(newestUndoable(stack)).toBeNull();
  });

  it('steps over one whose card had moved on', () => {
    expect(newestUndoable(stacked('moved', 'refused'))?.id).toBe(1);
  });

  it('offers a failed one again', () => {
    expect(newestUndoable(stacked('moved', 'failed'))?.id).toBe(2);
  });

  /* A second keystroke must not run ahead of the first. */
  it('is nothing while the newest is still in flight', () => {
    expect(newestUndoable(stacked('moved', 'undoing'))).toBeNull();
  });

  it('is nothing on an empty stack or one of filings', () => {
    expect(newestUndoable([])).toBeNull();
    expect(newestUndoable(stacked('filed', 'filed'))).toBeNull();
  });
});

const status = (id: number, name: string): Status => ({ id, name }) as Status;
const cardIn = (key: string, statusId: number, rank: number): IssueCard =>
  ({ key, title: `title ${key}`, statusId, rank, expedited: false }) as IssueCard;

const board: Board = {
  statuses: [status(1, 'Backlog'), status(2, 'To Do')],
  issues: [cardIn('AER-1', 1, 1024), cardIn('AER-2', 1, 2048), cardIn('AER-3', 1, 3072), cardIn('AER-9', 2, 1024)],
} as Board;

describe('dropConfirmation', () => {
  it('says where the card came from and how to put it back', () => {
    expect(dropConfirmation(board, 'AER-2', 2)).toEqual({
      issueKey: 'AER-2',
      title: 'title AER-2',
      from: { id: 1, name: 'Backlog' },
      to: { id: 2, name: 'To Do' },
      restore: { statusId: 1, afterKey: 'AER-1', beforeKey: 'AER-3', fromStatusId: 2 },
    });
  });

  /* Dropped into the column it was already in: a reorder, which the server
     writes no event for, and which there is nothing to confirm. */
  it('earns nothing for a reorder within a column', () => {
    expect(dropConfirmation(board, 'AER-2', 1)).toBeNull();
  });

  it('earns nothing for a card or a column that is not on the board', () => {
    expect(dropConfirmation(board, 'AER-404', 2)).toBeNull();
    expect(dropConfirmation(board, 'AER-2', 99)).toBeNull();
  });
});

const entry = (key: string, statusId: number, rank: number): CascadeEntry => ({
  key,
  rank,
  restore: { statusId, afterKey: null, beforeKey: null, fromStatusId: 9 },
});

describe('withCascade', () => {
  it('attaches what the offer moved to the chicklet for that card', () => {
    const stack = withCascade(stacked('moved'), 'AER-1', [entry('AER-5', 1, 1024)]);

    expect((stack[0] as MovedConfirmation).cascade.map((e) => e.key)).toEqual(['AER-5']);
  });

  /* Dragging the same epic twice is two chicklets, and the offer was asked
     about the second. */
  it('attaches to the newest chicklet for the key and to no other', () => {
    const stack = withCascade(raiseMove(stacked('moved'), drop('AER-1'), 2), 'AER-1', [entry('AER-5', 1, 1024)]);

    expect((stack[0] as MovedConfirmation).cascade).toHaveLength(1);
    expect((stack[1] as MovedConfirmation).cascade).toHaveLength(0);
  });

  /* Confirm pressed again after a partial refusal: what moved the first time
     comes back unchanged and is not a new entry. */
  it('merges a second press by key rather than replacing the first', () => {
    let stack = withCascade(stacked('moved'), 'AER-1', [entry('AER-5', 1, 1024), entry('AER-6', 1, 2048)]);
    stack = withCascade(stack, 'AER-1', [entry('AER-6', 1, 2048), entry('AER-7', 1, 3072)]);

    expect((stack[0] as MovedConfirmation).cascade.map((e) => e.key)).toEqual(['AER-5', 'AER-6', 'AER-7']);
  });

  it('ignores a key with no chicklet, and a filing', () => {
    const stack = stacked('filed', 'moved');

    expect(withCascade(stack, 'AER-404', [entry('AER-5', 1, 1024)])).toEqual(stack);
    expect(withCascade(stack, 'AER-1', [entry('AER-5', 1, 1024)])).toEqual(stack);
  });

  it('does not touch the stack it was given', () => {
    const before = stacked('moved');
    withCascade(before, 'AER-1', [entry('AER-5', 1, 1024)]);

    expect((before[0] as MovedConfirmation).cascade).toEqual([]);
  });
});

describe('cascadeOrder', () => {
  it('runs each column top to bottom, so a card lands after one already home', () => {
    const ordered = cascadeOrder([entry('AER-8', 2, 2048), entry('AER-6', 1, 3072), entry('AER-7', 2, 1024), entry('AER-5', 1, 1024)]);

    expect(ordered.map((e) => e.key)).toEqual(['AER-5', 'AER-6', 'AER-7', 'AER-8']);
  });

  it('keeps adjacent descendants in their old order', () => {
    const ordered = cascadeOrder([entry('AER-3', 1, 3072), entry('AER-2', 1, 2048)]);

    expect(ordered.map((e) => e.key)).toEqual(['AER-2', 'AER-3']);
  });

  it('does not touch the list it was given', () => {
    const before = [entry('AER-3', 1, 3072), entry('AER-2', 1, 2048)];
    cascadeOrder(before);

    expect(before.map((e) => e.key)).toEqual(['AER-3', 'AER-2']);
  });
});

describe('cascadeEntries', () => {
  const DONE = 3;
  const wide: Board = {
    statuses: [status(1, 'Backlog'), status(2, 'To Do'), status(DONE, 'Done')],
    issues: [
      cardIn('AER-1', 1, 1024),
      cardIn('AER-2', 1, 2048),
      cardIn('AER-3', 1, 3072),
      cardIn('AER-4', DONE, 1024),
    ],
  } as Board;

  /* AER-4 is already done and never in the offer, so it is never in the list. */
  it('names where each card the offer would move stood, and the column it goes to', () => {
    const offer = { key: 'AER-1', column: status(DONE, 'Done'), cards: [wide.issues[1], wide.issues[2]] } as CloseOffer;

    expect(cascadeEntries(wide, offer)).toEqual([
      { key: 'AER-2', rank: 2048, restore: { statusId: 1, afterKey: 'AER-1', beforeKey: 'AER-3', fromStatusId: DONE } },
      { key: 'AER-3', rank: 3072, restore: { statusId: 1, afterKey: 'AER-2', beforeKey: null, fromStatusId: DONE } },
    ]);
  });
});

describe('undoneNote and cascadeClause', () => {
  const withEntries = (n: number): MovedConfirmation => ({
    ...(stacked('moved')[0] as MovedConfirmation),
    cascade: Array.from({ length: n }, (_, i) => entry(`AER-${i + 5}`, 1, 1024 * (i + 1))),
  });

  it('says only the move for a chicklet that closed nothing else', () => {
    expect(undoneNote(withEntries(0), [])).toBe('moved back to Backlog');
    expect(cascadeClause(withEntries(0))).toBe('');
  });

  it('counts what went back with it', () => {
    expect(undoneNote(withEntries(3), [])).toBe('moved back to Backlog, with 3 under it');
    expect(cascadeClause(withEntries(3))).toBe(', and 3 under it closed');
  });

  it('names each that stayed, with its reason, and counts only the rest', () => {
    const note = undoneNote(withEntries(3), ['AER-6 is in To Do now - nothing moved']);

    expect(note).toBe('moved back to Backlog, with 2 under it. Left where they are: AER-6 is in To Do now - nothing moved');
  });

  it('says none went back when every one stayed', () => {
    expect(undoneNote(withEntries(1), ['AER-5 - down'])).toBe('moved back to Backlog. Left where they are: AER-5 - down');
  });
});

/* Criterion 4.3 and 4.4 - a reload starts the corner empty, and a chicklet
   does not turn up in a second tab - hold because the stack is never written
   anywhere a second document could read it. Asserted rather than assumed, so
   a later "remember these across a reload" has to argue with a test. */
describe('the stack lives only in the page', () => {
  it('reads and writes no browser storage', () => {
    const touched: string[] = [];
    const trap = new Proxy(
      {},
      {
        get: (_t, prop) => {
          touched.push(String(prop));
          return undefined;
        },
        set: (_t, prop) => {
          touched.push(String(prop));
          return true;
        },
      },
    );

    const globals = globalThis as unknown as Record<string, unknown>;
    globals.localStorage = trap;
    globals.sessionStorage = trap;
    try {
      dismiss(raise(raise([], { key: 'AER-12', title: 'one' }, 1), { key: 'AER-13', title: 'two' }, 2), 1);
    } finally {
      delete globals.localStorage;
      delete globals.sessionStorage;
    }

    expect(touched).toEqual([]);
  });
});
