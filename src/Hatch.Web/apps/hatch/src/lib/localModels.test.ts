import { describe, expect, it } from 'vitest';
import { heldLine, localModelRequest } from './localModels';

const GB = 1024 ** 3;
const draft = (ollamaId: string, contextLength: string, footprintGigabytes: string) => ({
  ollamaId,
  contextLength,
  footprintGigabytes,
});

describe('localModelRequest', () => {
  it('trims the id and turns gigabytes into bytes', () => {
    expect(localModelRequest(draft('  fake:7b ', '32768', '4.5'))).toEqual({
      ollamaId: 'fake:7b',
      contextLengthTokens: 32768,
      memoryFootprintBytes: 4.5 * GB,
    });
  });

  it('is null for an empty id, an empty number, zero or a negative', () => {
    expect(localModelRequest(draft('', '8192', '4'))).toBeNull();
    expect(localModelRequest(draft('   ', '8192', '4'))).toBeNull();
    expect(localModelRequest(draft('fake:1b', '', '4'))).toBeNull();
    expect(localModelRequest(draft('fake:1b', '8192', ''))).toBeNull();
    expect(localModelRequest(draft('fake:1b', '0', '4'))).toBeNull();
    expect(localModelRequest(draft('fake:1b', '8192', '0'))).toBeNull();
    expect(localModelRequest(draft('fake:1b', '-1', '4'))).toBeNull();
    expect(localModelRequest(draft('fake:1b', '8192', '-2'))).toBeNull();
  });

  it('is null for a fractional context length or a number that is not one', () => {
    expect(localModelRequest(draft('fake:1b', '8192.5', '4'))).toBeNull();
    expect(localModelRequest(draft('fake:1b', 'lots', '4'))).toBeNull();
  });
});

describe('heldLine', () => {
  it('is null for an empty hold', () => {
    expect(heldLine([], ['fake:1b'])).toBeNull();
  });

  it("names the ids in the hold's own order, with no parenthesis when none is pulled", () => {
    expect(heldLine(['fake:7b', 'fake:1b'], [])).toBe('Holds fake:7b, fake:1b');
  });

  it('marks the ones the runner already lists', () => {
    expect(heldLine(['fake:1b', 'fake:7b'], ['fake:1b', 'other:3b'])).toBe('Holds fake:1b, fake:7b (fake:1b pulled)');
  });

  it('marks every one when all are pulled', () => {
    expect(heldLine(['fake:1b', 'fake:7b'], ['fake:7b', 'fake:1b'])).toBe(
      'Holds fake:1b, fake:7b (fake:1b, fake:7b pulled)',
    );
  });
});
