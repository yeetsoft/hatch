import { describe, expect, it } from 'vitest';
import { refusalSentence } from './errors';

const FALLBACK = 'POST /api/hatch/issues/AER-1/move failed: 409 Conflict';

describe('refusalSentence', () => {
  it('reads a bare JSON string as the sentence', () => {
    expect(refusalSentence('"AER-1 is in another project"', FALLBACK)).toBe('AER-1 is in another project');
  });

  it('reads a structured body carrying just { error }', () => {
    expect(refusalSentence('{"error":"the WIP section is full - 2 of 2 stories and bugs are in it"}', FALLBACK)).toBe(
      'the WIP section is full - 2 of 2 stories and bugs are in it',
    );
  });

  it('reads { error, load, limit } and ignores the numbers', () => {
    const body = '{"error":"the WIP section is full - 2 of 2 stories and bugs are in it","load":2,"limit":2}';
    expect(refusalSentence(body, FALLBACK)).toBe('the WIP section is full - 2 of 2 stories and bugs are in it');
  });

  it('falls back on an object with no string error', () => {
    expect(refusalSentence('{"type":"about:blank","title":"Bad Request","status":400}', FALLBACK)).toBe(FALLBACK);
  });

  it('falls back on HTML', () => {
    expect(refusalSentence('<html><body>502 Bad Gateway</body></html>', FALLBACK)).toBe(FALLBACK);
  });

  it('falls back on an empty body', () => {
    expect(refusalSentence('', FALLBACK)).toBe(FALLBACK);
  });
});
