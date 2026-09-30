import { describe, expect, it } from 'vitest';
import { editDraft, isDirty, openDraft, receiveKnown, revert, type Draft } from './draft';

describe('isDirty', () => {
  it('is false on a freshly opened draft', () => {
    expect(isDirty(openDraft('one'))).toBe(false);
  });

  it('is true once the text differs from what is known', () => {
    expect(isDirty(editDraft(openDraft('one'), 'two'))).toBe(true);
  });

  it('is false when the only difference is line endings', () => {
    const draft: Draft = { known: 'a\nb', text: 'a\r\nb', changed: false };
    expect(isDirty(draft)).toBe(false);
  });
});

describe('receiveKnown', () => {
  it('is a no-op when the value has not moved', () => {
    const draft = editDraft(openDraft('one'), 'one edited');
    expect(receiveKnown(draft, 'one')).toBe(draft);
  });

  it('adopts the new value silently on a clean draft', () => {
    expect(receiveKnown(openDraft('one'), 'two')).toEqual(openDraft('two'));
  });

  it('keeps the text, updates known, and flags changed on a dirty draft', () => {
    const draft = editDraft(openDraft('one'), 'one edited');
    expect(receiveKnown(draft, 'two')).toEqual({ known: 'two', text: 'one edited', changed: true });
  });

  it('updates known again and stays changed on a second move while already changed', () => {
    const draft = receiveKnown(editDraft(openDraft('one'), 'one edited'), 'two');
    expect(receiveKnown(draft, 'three')).toEqual({ known: 'three', text: 'one edited', changed: true });
  });
});

describe('revert', () => {
  it('drops the typed text and clears changed', () => {
    const draft: Draft = { known: 'two', text: 'one edited', changed: true };
    expect(revert(draft)).toEqual(openDraft('two'));
  });
});
