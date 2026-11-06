import { describe, expect, it } from 'vitest';
import {
  DREAMS_PAGE,
  EMPTY_DREAMS,
  WEAVE_MODES,
  countLine,
  dreamsRequest,
  firstLine,
  hasMore,
  isUrl,
  readDreams,
  removeDream,
  replaceDream,
  showsDream,
  weaveProjectDefault,
  weaveRequest,
  weaveSeed,
  writeDreams,
  type DreamFilters,
} from './dreams';
import type { Dream, Project } from '../types';

function params(entries: Record<string, string>): URLSearchParams {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(entries)) params.set(key, value);
  return params;
}

describe('readDreams', () => {
  it('reads an empty params as the empty filter', () => {
    expect(readDreams(params({}))).toEqual(EMPTY_DREAMS);
  });

  it('reads q as typed', () => {
    expect(readDreams(params({ q: '  needle  ' })).q).toBe('  needle  ');
  });

  it('reads a positive integer projectId as itself', () => {
    expect(readDreams(params({ projectId: '12' })).projectId).toBe('12');
  });

  it.each(['0', '-1', '1.5', 'abc'])('reads an invalid projectId %s as empty', (raw) => {
    expect(readDreams(params({ projectId: raw })).projectId).toBe('');
  });

  it('reads loose=true as true', () => {
    expect(readDreams(params({ loose: 'true' })).loose).toBe(true);
  });

  it('reads loose=false as false', () => {
    expect(readDreams(params({ loose: 'false' })).loose).toBe(false);
  });

  it('reads an absent loose as false', () => {
    expect(readDreams(params({})).loose).toBe(false);
  });

  it('reads issue=HA-1 as issueKey', () => {
    expect(readDreams(params({ issue: 'HA-1' })).issueKey).toBe('HA-1');
  });

  it('reads an absent issue as an empty issueKey', () => {
    expect(readDreams(params({})).issueKey).toBe('');
  });
});

describe('writeDreams', () => {
  it('omits a blank q', () => {
    expect(writeDreams({ ...EMPTY_DREAMS, q: '' }).has('q')).toBe(false);
  });

  it('omits a whitespace-only q', () => {
    expect(writeDreams({ ...EMPTY_DREAMS, q: '   ' }).has('q')).toBe(false);
  });

  it('omits loose when false', () => {
    expect(writeDreams({ ...EMPTY_DREAMS, loose: false }).has('loose')).toBe(false);
  });

  it('writes loose as the string true when true', () => {
    expect(writeDreams({ ...EMPTY_DREAMS, loose: true }).get('loose')).toBe('true');
  });

  it('writes issueKey under the key issue', () => {
    const written = writeDreams({ ...EMPTY_DREAMS, issueKey: 'HA-1' });
    expect(written.get('issue')).toBe('HA-1');
    expect(written.has('issueKey')).toBe(false);
  });

  it('writes a full filter in the fixed order', () => {
    const full: DreamFilters = { q: 'needle', projectId: '1', loose: true, issueKey: 'HA-1' };

    expect([...writeDreams(full).keys()]).toEqual(['q', 'projectId', 'loose', 'issue']);
  });

  it('round-trips a valid filter through readDreams', () => {
    const full: DreamFilters = { q: 'needle', projectId: '1', loose: true, issueKey: 'HA-1' };

    expect(readDreams(writeDreams(full))).toEqual(full);
  });
});

describe('dreamsRequest', () => {
  it('always writes limit=50', () => {
    expect(dreamsRequest(EMPTY_DREAMS, 0).get('limit')).toBe(String(DREAMS_PAGE));
  });

  it('omits offset 0', () => {
    expect(dreamsRequest(EMPTY_DREAMS, 0).has('offset')).toBe(false);
  });

  it('writes a non-zero offset', () => {
    expect(dreamsRequest(EMPTY_DREAMS, 50).get('offset')).toBe('50');
  });

  it('renames issue to issueKey', () => {
    const request = dreamsRequest({ ...EMPTY_DREAMS, issueKey: 'HA-1' }, 0);
    expect(request.get('issueKey')).toBe('HA-1');
    expect(request.has('issue')).toBe(false);
  });

  it('carries q, projectId and loose through unrenamed', () => {
    const request = dreamsRequest({ q: 'needle', projectId: '1', loose: true, issueKey: '' }, 0);
    expect(request.get('q')).toBe('needle');
    expect(request.get('projectId')).toBe('1');
    expect(request.get('loose')).toBe('true');
  });
});

describe('firstLine', () => {
  it('takes the first line of a multi-line body', () => {
    expect(firstLine('First line.\nSecond line.')).toBe('First line.');
  });

  it('is the whole body, trimmed, for a one-line body', () => {
    expect(firstLine('  One line.  ')).toBe('One line.');
  });
});

describe('isUrl', () => {
  it('is true for a real address', () => {
    expect(isUrl('https://example.com')).toBe(true);
  });

  it('is true for an http address', () => {
    expect(isUrl('http://x.test')).toBe(true);
  });

  it('is false for a javascript: source, which parses but must not become a link', () => {
    expect(isUrl('javascript:alert(1)')).toBe(false);
  });

  it('is false for a mailto: source', () => {
    expect(isUrl('mailto:a@b.test')).toBe(false);
  });

  it('is false for plain text', () => {
    expect(isUrl('not a url')).toBe(false);
  });

  it('is false for an empty string', () => {
    expect(isUrl('')).toBe(false);
  });
});

describe('countLine', () => {
  it('reads "0 results" for none', () => {
    expect(countLine(0, 0)).toBe('0 results');
  });

  it('reads how many of the total are shown', () => {
    expect(countLine(3, 340)).toBe('3 of 340');
  });
});

describe('hasMore', () => {
  it('is true while fewer are shown than match', () => {
    expect(hasMore(50, 51)).toBe(true);
  });

  it('is false once all are shown', () => {
    expect(hasMore(51, 51)).toBe(false);
  });
});

describe('showsDream', () => {
  it('shows a dream under the empty filter', () => {
    expect(showsDream(EMPTY_DREAMS, { projectId: null })).toBe(true);
  });

  it('shows a dream under loose alone, since a new dream has no issue woven in', () => {
    expect(showsDream({ ...EMPTY_DREAMS, loose: true }, { projectId: null })).toBe(true);
  });

  it('hides a dream under a q', () => {
    expect(showsDream({ ...EMPTY_DREAMS, q: 'x' }, { projectId: null })).toBe(false);
  });

  it('shows a dream under a whitespace-only q, which writes nothing', () => {
    expect(showsDream({ ...EMPTY_DREAMS, q: '   ' }, { projectId: null })).toBe(true);
  });

  it('hides a dream under an issueKey', () => {
    expect(showsDream({ ...EMPTY_DREAMS, issueKey: 'HA-1' }, { projectId: null })).toBe(false);
  });

  it('shows a dream whose project is the one filtered on', () => {
    expect(showsDream({ ...EMPTY_DREAMS, projectId: '3' }, { projectId: 3 })).toBe(true);
  });

  it('hides a dream whose project differs from the one filtered on', () => {
    expect(showsDream({ ...EMPTY_DREAMS, projectId: '3' }, { projectId: 4 })).toBe(false);
  });

  it('hides a loose-leaning dream under a project filter', () => {
    expect(showsDream({ ...EMPTY_DREAMS, projectId: '3' }, { projectId: null })).toBe(false);
  });
});

describe('replaceDream', () => {
  const a = { id: 1, body: 'a' } as Dream;
  const b = { id: 2, body: 'b' } as Dream;
  const c = { id: 3, body: 'c' } as Dream;

  it('swaps the dream with the same id and keeps the order', () => {
    const b2 = { ...b, body: 'b2' };
    expect(replaceDream([a, b, c], b2)).toEqual([a, b2, c]);
  });

  it('leaves the other dreams as the same objects', () => {
    const result = replaceDream([a, b, c], { ...b, body: 'b2' });
    expect(result[0]).toBe(a);
    expect(result[2]).toBe(c);
  });

  it('changes nothing for an id the list does not hold', () => {
    expect(replaceDream([a, b], { ...c, body: 'c2' })).toEqual([a, b]);
  });

  it('does not mutate its input', () => {
    const input = [a, b];
    replaceDream(input, { ...b, body: 'b2' });
    expect(input).toEqual([a, b]);
  });
});

describe('removeDream', () => {
  const a = { id: 1 } as Dream;
  const b = { id: 2 } as Dream;
  const c = { id: 3 } as Dream;

  it('drops only the dream with that id', () => {
    expect(removeDream([a, b, c], 2)).toEqual([a, c]);
  });

  it('changes nothing for an id the list does not hold', () => {
    expect(removeDream([a, b], 9)).toEqual([a, b]);
  });

  it('does not mutate its input', () => {
    const input = [a, b];
    removeDream(input, 1);
    expect(input).toEqual([a, b]);
  });
});

describe('weaveSeed', () => {
  it('follows q while the seed box has not been typed in', () => {
    expect(weaveSeed(null, 'needle')).toBe('needle');
  });

  it('follows a changing q, untyped', () => {
    expect(weaveSeed(null, '')).toBe('');
  });

  it('keeps an empty typed value rather than falling back to q', () => {
    expect(weaveSeed('', 'needle')).toBe('');
  });

  it('keeps the person\'s own text once typed', () => {
    expect(weaveSeed('their own seed', 'needle')).toBe('their own seed');
  });
});

describe('weaveRequest', () => {
  const dreams = [{ id: 3 } as Dream, { id: 1 } as Dream, { id: 2 } as Dream];

  it('is null with no project chosen', () => {
    expect(
      weaveRequest({ mode: 'seed', seed: 'x', q: '', projectId: null, useOnScreen: false, dreams: [] }),
    ).toBeNull();
  });

  it('is null for mode seed with a blank seed', () => {
    expect(
      weaveRequest({ mode: 'seed', seed: '   ', q: '', projectId: 1, useOnScreen: false, dreams: [] }),
    ).toBeNull();
  });

  it('is null for mode seed with q blank too, once untyped', () => {
    expect(
      weaveRequest({ mode: 'seed', seed: null, q: '  ', projectId: 1, useOnScreen: false, dreams: [] }),
    ).toBeNull();
  });

  it('sends the trimmed seed in mode seed', () => {
    expect(
      weaveRequest({ mode: 'seed', seed: '  a seed  ', q: '', projectId: 1, useOnScreen: false, dreams: [] }),
    ).toEqual({ projectId: 1, mode: 'seed', seed: 'a seed' });
  });

  it('sends no seed for the other modes even if one is typed', () => {
    expect(
      weaveRequest({ mode: 'pressing', seed: 'typed but unused', q: '', projectId: 1, useOnScreen: false, dreams: [] }),
    ).toEqual({ projectId: 1, mode: 'pressing' });
  });

  it('omits dreamIds unless asked', () => {
    expect(
      weaveRequest({ mode: 'common', seed: null, q: '', projectId: 1, useOnScreen: false, dreams }),
    ).toEqual({ projectId: 1, mode: 'common' });
  });

  it('omits dreamIds when the list is empty even if asked', () => {
    expect(
      weaveRequest({ mode: 'common', seed: null, q: '', projectId: 1, useOnScreen: true, dreams: [] }),
    ).toEqual({ projectId: 1, mode: 'common' });
  });

  it('lists dreamIds in list order when asked and non-empty', () => {
    expect(
      weaveRequest({ mode: 'next', seed: null, q: '', projectId: 1, useOnScreen: true, dreams }),
    ).toEqual({ projectId: 1, mode: 'next', dreamIds: [3, 1, 2] });
  });
});

describe('WEAVE_MODES', () => {
  it('lists the four modes in the order the control offers them', () => {
    expect(WEAVE_MODES).toEqual(['seed', 'pressing', 'common', 'next']);
  });
});

describe('weaveProjectDefault', () => {
  const projects = [{ id: 1 } as Project, { id: 2 } as Project];

  it('takes the facet when it parses and names a known project', () => {
    expect(weaveProjectDefault('2', projects)).toBe(2);
  });

  it('falls back to the sole project when there is exactly one', () => {
    expect(weaveProjectDefault('', [{ id: 5 } as Project])).toBe(5);
  });

  it('is null for several projects with no facet', () => {
    expect(weaveProjectDefault('', projects)).toBeNull();
  });

  it('is null for a facet naming an unknown project', () => {
    expect(weaveProjectDefault('9', projects)).toBeNull();
  });
});
