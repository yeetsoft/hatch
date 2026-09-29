import { describe, expect, it } from 'vitest';
import { HttpError } from './errors';
import { overridden, overrideLine, refusalText, wipRefusal } from './wipOverride';

const SENTENCE = 'the WIP section is full - 5 of 5 stories and bugs are in it';

describe('wipRefusal', () => {
  it('reads a 409 whose body carries { error, load, limit }', () => {
    const err = new HttpError(SENTENCE, 409, { error: SENTENCE, load: 5, limit: 5 });
    expect(wipRefusal(err)).toEqual({ error: SENTENCE, load: 5, limit: 5 });
  });

  it('returns null for a bare-string 409 body - the stale fromStatusId refusal', () => {
    const err = new HttpError('AER-1 is not where you left it', 409, 'AER-1 is not where you left it');
    expect(wipRefusal(err)).toBeNull();
  });

  it('returns null for a 409 whose body has only one of the numbers', () => {
    const err = new HttpError(SENTENCE, 409, { error: SENTENCE, load: 5 });
    expect(wipRefusal(err)).toBeNull();
  });

  it('returns null for any other status', () => {
    const err = new HttpError(SENTENCE, 500, { error: SENTENCE, load: 5, limit: 5 });
    expect(wipRefusal(err)).toBeNull();
  });

  it('returns null for a plain Error with no body at all', () => {
    expect(wipRefusal(new Error(SENTENCE))).toBeNull();
  });
});

describe('overridden', () => {
  it('adds wipOverride: true and keeps every other field of a move request', () => {
    const request = { statusId: 2, afterKey: 'AER-1', beforeKey: null, fromStatusId: 1 };
    expect(overridden(request)).toEqual({ ...request, wipOverride: true });
  });

  it('adds wipOverride: true to a bare patch request', () => {
    expect(overridden({ statusId: 2 })).toEqual({ statusId: 2, wipOverride: true });
  });
});

describe('refusalText', () => {
  it('upper-cases the first letter and adds a trailing period', () => {
    expect(refusalText({ error: SENTENCE, load: 5, limit: 5 })).toBe(
      'The WIP section is full - 5 of 5 stories and bugs are in it.',
    );
  });
});

describe('overrideLine', () => {
  it('draws the override line from a wip_overridden payload', () => {
    expect(overrideLine({ limit: 5, load: 6, to: 'In Progress' })).toBe(
      'overrode the WIP limit — 6 of 5 into In Progress',
    );
  });

  it('returns null when the payload is missing a numeric field or to', () => {
    expect(overrideLine({ limit: 5, to: 'In Progress' })).toBeNull();
    expect(overrideLine({ limit: 5, load: 6 })).toBeNull();
    expect(overrideLine(null)).toBeNull();
  });
});
