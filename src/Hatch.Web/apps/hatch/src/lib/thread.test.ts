import { describe, expect, it } from 'vitest';
import { NEWEST_FIRST_LABEL, NEWEST_FIRST_TITLE, newestFirst } from './thread';
import type { Comment } from '../types';

let next = 1;

const comment = (body: string): Comment => ({
  id: next++,
  author: 'hatch-agent',
  body,
  kind: '',
  answersId: null,
  options: null,
  createdAt: '2026-09-05T00:00:00Z',
  deliveredAt: null,
  deliveredTo: null,
  level: null,
  answeredAfter: false,
  authorKind: null,
});

describe('newestFirst', () => {
  it('draws nothing from an empty thread', () => {
    expect(newestFirst([])).toEqual([]);
  });

  it('puts the last thing said first', () => {
    const thread = [comment('first'), comment('second'), comment('third')];

    expect(newestFirst(thread).map((c) => c.body)).toEqual(['third', 'second', 'first']);
  });

  it('leaves the thread it was given in the order it came', () => {
    const thread = [comment('first'), comment('second')];

    newestFirst(thread);

    expect(thread.map((c) => c.body)).toEqual(['first', 'second']);
  });
});

describe('the newest-first wording', () => {
  it('says "newest first" to a screen reader and "Newest first" on hover', () => {
    expect(NEWEST_FIRST_LABEL).toBe('newest first');
    expect(NEWEST_FIRST_TITLE).toBe('Newest first');
  });
});
