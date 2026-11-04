import { describe, expect, it } from 'vitest';
import {
  DREAMS_PAGE,
  EMPTY_DREAMS,
  countLine,
  dreamsRequest,
  firstLine,
  hasMore,
  isUrl,
  readDreams,
  writeDreams,
  type DreamFilters,
} from './dreams';

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
