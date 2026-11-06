import { describe, expect, it } from 'vitest';
import { attachmentUrl, chunks, formatSize, linkedAttachmentIds, missingIds, settle } from './attachmentList';
import type { Attachment } from '../types';

const ID_A = '11111111-1111-1111-1111-111111111111';
const ID_B = '22222222-2222-2222-2222-222222222222';
const ID_C = '33333333-3333-3333-3333-333333333333';

function attachment(id: string, overrides: Partial<Attachment> = {}): Attachment {
  return { id, fileName: 'file', contentType: 'application/pdf', size: 1, createdAt: '2026-01-01', ...overrides };
}

describe('attachmentUrl', () => {
  it('is the same binding descriptionImage.ts exports', () => {
    expect(attachmentUrl('abc')).toBe('/api/hatch/attachments/abc');
  });
});

describe('linkedAttachmentIds', () => {
  it('keeps order of first appearance', () => {
    const markdown = `[b](/api/hatch/attachments/${ID_B}) [a](/api/hatch/attachments/${ID_A})`;
    expect(linkedAttachmentIds(markdown)).toEqual([ID_B, ID_A]);
  });

  it('collapses duplicates, including the same id in different case, to one lower-case entry', () => {
    const markdown = `/api/hatch/attachments/${ID_A} /api/hatch/attachments/${ID_A.toUpperCase()}`;
    expect(linkedAttachmentIds(markdown)).toEqual([ID_A]);
  });

  it('finds an image, a link and a bare path together in one call', () => {
    const markdown = [
      `![alt](/api/hatch/images/${ID_A})`,
      `[text](/api/hatch/attachments/${ID_B})`,
      `see /api/hatch/attachments/${ID_C} for more`,
    ].join('\n');
    expect(linkedAttachmentIds(markdown)).toEqual([ID_A, ID_B, ID_C]);
  });

  it('lower-cases an upper-case guid', () => {
    expect(linkedAttachmentIds(`/api/hatch/images/${ID_A.toUpperCase()}`)).toEqual([ID_A]);
  });

  it('matches a full URL, a host in front is just more text before the match', () => {
    expect(linkedAttachmentIds(`https://host/api/hatch/images/${ID_A}`)).toEqual([ID_A]);
  });

  it('is empty for no match anywhere', () => {
    expect(linkedAttachmentIds('no attachments here')).toEqual([]);
    expect(linkedAttachmentIds('')).toEqual([]);
  });

  it('is empty for a query string form and a guid-shaped string that fails the hex groups', () => {
    const markdown = `/api/hatch/attachments?ids=${ID_A} /api/hatch/images/not-a-guid`;
    expect(linkedAttachmentIds(markdown)).toEqual([]);
  });

  it('still lists a guid inside a fenced code span, since this is a regex scan, not a markdown parse', () => {
    const markdown = ['```', `/api/hatch/attachments/${ID_A}`, '```'].join('\n');
    expect(linkedAttachmentIds(markdown)).toEqual([ID_A]);
  });
});

describe('formatSize', () => {
  it('is 0 B for zero, negative and NaN', () => {
    expect(formatSize(0)).toBe('0 B');
    expect(formatSize(-5)).toBe('0 B');
    expect(formatSize(NaN)).toBe('0 B');
  });

  it('is whole bytes under 1 KB', () => {
    expect(formatSize(512)).toBe('512 B');
    expect(formatSize(1023)).toBe('1023 B');
  });

  it('is KB at and above 1024 bytes', () => {
    expect(formatSize(1024)).toBe('1 KB');
    expect(formatSize(1536)).toBe('1.5 KB');
  });

  it('drops the decimal at and above 10 of a unit', () => {
    expect(formatSize(12 * 1024)).toBe('12 KB');
  });

  it('keeps one decimal in MB and GB', () => {
    expect(formatSize(2.3 * 1024 * 1024)).toBe('2.3 MB');
    expect(formatSize(1.1 * 1024 * 1024 * 1024)).toBe('1.1 GB');
  });
});

describe('missingIds', () => {
  it('skips an id known as a file, an id known as null, and an id in flight, keeping the rest in order', () => {
    const known = new Map([
      [ID_A, attachment(ID_A)],
      [ID_B, null],
    ]);
    const inFlight = new Set([ID_C]);
    const ids = [ID_A, ID_B, ID_C, '44444444-4444-4444-4444-444444444444'];
    expect(missingIds(ids, known, inFlight)).toEqual(['44444444-4444-4444-4444-444444444444']);
  });
});

describe('settle', () => {
  it('lands an asked id found under its own lower-cased id', () => {
    const result = settle(new Map(), [ID_A], [attachment(ID_A.toUpperCase())]);
    expect(result.get(ID_A)).toEqual(attachment(ID_A.toUpperCase()));
  });

  it('lands an asked id not in found as null', () => {
    const result = settle(new Map(), [ID_A], []);
    expect(result.get(ID_A)).toBeNull();
  });

  it('leaves an existing entry not in asked unchanged, and never mutates the input map', () => {
    const known = new Map([[ID_B, attachment(ID_B)]]);
    const result = settle(known, [ID_A], [attachment(ID_A)]);
    expect(result.get(ID_B)).toEqual(attachment(ID_B));
    expect(known.size).toBe(1);
    expect(known.has(ID_A)).toBe(false);
  });
});

describe('chunks', () => {
  it('splits into runs of at most size', () => {
    const result = chunks(Array.from({ length: 250 }, (_, i) => i), 100);
    expect(result.map((c) => c.length)).toEqual([100, 100, 50]);
  });

  it('is empty for an empty input', () => {
    expect(chunks([], 100)).toEqual([]);
  });
});
