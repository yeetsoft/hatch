import { closestCorners } from '@dnd-kit/core';
import type { ClientRect, Collision, CollisionDetection, DroppableContainer } from '@dnd-kit/core';
import { describe, expect, it } from 'vitest';
import { aimAt } from './aim';
import { columnDroppableId } from './place';
import type { IssueCard } from '../types';

const DRAFT = 1;
const BREAKDOWN = 2;

const card = (key: string, statusId: number): IssueCard => ({
  key,
  projectKey: 'AER',
  type: 'task',
  title: key,
  statusId,
  rank: 0,
  parentKey: null,
  readyAt: null,
  dueAt: null,
  openQuestions: 0,
  assignee: null,
  claim: null,
  expedited: false,
});

const rect = (left: number, top: number, width: number, height: number): ClientRect => ({
  left,
  top,
  width,
  height,
  right: left + width,
  bottom: top + height,
});

type CDArgs = Parameters<CollisionDetection>[0];

/* No DOM: droppableRects and droppableContainers are hand-built from a plain
   id-to-rect map, the way lib/place.test.ts hand-builds board state instead of
   rendering it. */
function argsFor(
  geometry: Record<string, ClientRect>,
  pointerCoordinates: { x: number; y: number } | null,
  collisionRect: ClientRect = rect(0, 0, 0, 0),
): CDArgs {
  return {
    active: { id: 'dragging' } as unknown as CDArgs['active'],
    collisionRect,
    droppableRects: new Map(Object.entries(geometry)),
    droppableContainers: Object.keys(geometry).map((id) => ({ id }) as unknown as DroppableContainer),
    pointerCoordinates,
  };
}

const idsOf = (hits: Collision[]) => hits.map((h) => String(h.id));

/* The reported bug, pinned: two adjacent 190px-wide, full-height columns, five
   cards in Draft and two at the top of Breakdown. DRAFT-4 is dragged straight
   across into Breakdown at its own height - closestCorners reads that as
   dropped back on its own slot, because DRAFT-4 sits at the same height as the
   preview and only a column's width away, while Breakdown's own two cards and
   the columns themselves are all further off. */
describe('aimAt, the reported bug pinned', () => {
  const cards = [
    card('DRAFT-1', DRAFT),
    card('DRAFT-2', DRAFT),
    card('DRAFT-3', DRAFT),
    card('DRAFT-4', DRAFT),
    card('DRAFT-5', DRAFT),
    card('BREAK-1', BREAKDOWN),
    card('BREAK-2', BREAKDOWN),
  ];

  const geometry = {
    [columnDroppableId(DRAFT)]: rect(0, 0, 190, 800),
    [columnDroppableId(BREAKDOWN)]: rect(190, 0, 190, 800),
    'DRAFT-1': rect(8, 40, 174, 60),
    'DRAFT-2': rect(8, 150, 174, 60),
    'DRAFT-3': rect(8, 260, 174, 60),
    'DRAFT-4': rect(8, 370, 174, 60),
    'DRAFT-5': rect(8, 480, 174, 60),
    'BREAK-1': rect(198, 40, 174, 60),
    'BREAK-2': rect(198, 150, 174, 60),
  };

  // DRAFT-4's own size, dragged straight across into Breakdown at the same
  // height - the pointer sits at its centre.
  const preview = rect(198, 370, 174, 60);
  const pointer = { x: 285, y: 400 };

  it('closestCorners reads this as dropped back on its own slot', () => {
    const hits = closestCorners(argsFor(geometry, pointer, preview));
    expect(idsOf(hits)[0]).toBe('DRAFT-4');
  });

  it('aimAt reads the column the pointer is actually over', () => {
    const hits = aimAt(cards)(argsFor(geometry, pointer, preview));
    expect(idsOf(hits)[0]).toBe(columnDroppableId(BREAKDOWN));
  });
});

describe('aimAt, pointer within a column holding cards', () => {
  const cards = [card('B-1', BREAKDOWN), card('B-2', BREAKDOWN)];

  const geometry = {
    [columnDroppableId(DRAFT)]: rect(0, 0, 190, 600),
    [columnDroppableId(BREAKDOWN)]: rect(190, 0, 190, 600),
    'B-1': rect(198, 40, 174, 60), // bottom 100
    'B-2': rect(198, 150, 174, 60), // top 150, bottom 210
  };

  const at = (x: number, y: number) => aimAt(cards)(argsFor(geometry, { x, y }));

  it('names the card the pointer is directly over', () => {
    expect(idsOf(at(285, 180))).toEqual(['B-2']);
  });

  it('names the column when the pointer is below the last card', () => {
    expect(idsOf(at(285, 300))).toEqual([columnDroppableId(BREAKDOWN)]);
  });

  it('names the nearer card in the gap between two', () => {
    // The gap runs 100-150; B-1's centre is at 70, B-2's at 180 - 135 is
    // nearer to B-2.
    expect(idsOf(at(285, 135))[0]).toBe('B-2');
  });

  it('names the topmost card when the pointer is over the heading', () => {
    expect(idsOf(at(285, 10))[0]).toBe('B-1');
  });
});

describe('aimAt, an empty column', () => {
  const cards = [card('D-1', DRAFT)];

  const geometry = {
    [columnDroppableId(DRAFT)]: rect(0, 0, 190, 600),
    [columnDroppableId(BREAKDOWN)]: rect(190, 0, 190, 600),
    'D-1': rect(8, 40, 174, 60),
  };

  it('is the column itself, with nothing inside it to aim at', () => {
    const hits = aimAt(cards)(argsFor(geometry, { x: 285, y: 300 }));
    expect(idsOf(hits)).toEqual([columnDroppableId(BREAKDOWN)]);
  });
});

describe('aimAt, pointer outside every column', () => {
  const geometry = {
    [columnDroppableId(DRAFT)]: rect(0, 0, 190, 600),
    [columnDroppableId(BREAKDOWN)]: rect(200, 0, 190, 600), // a 10px gap between them
  };

  it('lights nothing and moves nothing', () => {
    expect(aimAt([])(argsFor(geometry, { x: 195, y: 300 }))).toEqual([]);
  });
});

describe('aimAt, a keyboard drag', () => {
  const geometry = {
    [columnDroppableId(DRAFT)]: rect(0, 0, 190, 600),
    'D-1': rect(8, 40, 174, 60),
  };

  it('falls through to closestCorners, unchanged - a keyboard drag has no pointer', () => {
    const args = argsFor(geometry, null, rect(8, 100, 174, 60));
    expect(aimAt([card('D-1', DRAFT)])(args)).toEqual(closestCorners(args));
  });
});
