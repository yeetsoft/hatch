import { describe, expect, it } from 'vitest';
import { isTypingTarget, isUndoShortcut, undoShortcutLabel } from './shortcuts';

const chord = (key: string, mods: Partial<Record<'metaKey' | 'ctrlKey' | 'shiftKey' | 'altKey', boolean>> = {}) => ({
  key,
  metaKey: false,
  ctrlKey: false,
  shiftKey: false,
  altKey: false,
  ...mods,
});

describe('isUndoShortcut', () => {
  it('is Cmd+Z on a Mac and Ctrl+Z everywhere else', () => {
    expect(isUndoShortcut(chord('z', { metaKey: true }))).toBe(true);
    expect(isUndoShortcut(chord('z', { ctrlKey: true }))).toBe(true);
  });

  it('reads the capital a held Caps Lock sends', () => {
    expect(isUndoShortcut(chord('Z', { ctrlKey: true }))).toBe(true);
  });

  it('is not a bare z', () => {
    expect(isUndoShortcut(chord('z'))).toBe(false);
  });

  /* Redo is a different gesture, and nothing here answers it. */
  it('is not Redo', () => {
    expect(isUndoShortcut(chord('z', { metaKey: true, shiftKey: true }))).toBe(false);
    expect(isUndoShortcut(chord('Z', { ctrlKey: true, shiftKey: true }))).toBe(false);
    expect(isUndoShortcut(chord('y', { ctrlKey: true }))).toBe(false);
  });

  it('is not Alt+Ctrl+Z, which some layouts type a character with', () => {
    expect(isUndoShortcut(chord('z', { ctrlKey: true, altKey: true }))).toBe(false);
  });

  it('is not another key with the modifier held', () => {
    expect(isUndoShortcut(chord('x', { metaKey: true }))).toBe(false);
  });
});

describe('isTypingTarget', () => {
  it('is any place text is typed', () => {
    expect(isTypingTarget({ tagName: 'INPUT', type: 'text' })).toBe(true);
    expect(isTypingTarget({ tagName: 'INPUT', type: 'search' })).toBe(true);
    expect(isTypingTarget({ tagName: 'INPUT' })).toBe(true);
    expect(isTypingTarget({ tagName: 'TEXTAREA' })).toBe(true);
    expect(isTypingTarget({ tagName: 'SELECT' })).toBe(true);
    expect(isTypingTarget({ tagName: 'DIV', isContentEditable: true })).toBe(true);
  });

  it('is not an input that is a button in disguise', () => {
    for (const type of ['button', 'checkbox', 'radio', 'submit', 'range']) {
      expect(isTypingTarget({ tagName: 'INPUT', type })).toBe(false);
    }
  });

  it('is not the page, a button or a card', () => {
    expect(isTypingTarget(null)).toBe(false);
    expect(isTypingTarget({ tagName: 'BODY' })).toBe(false);
    expect(isTypingTarget({ tagName: 'BUTTON' })).toBe(false);
    expect(isTypingTarget({ tagName: 'DIV', isContentEditable: false })).toBe(false);
  });
});

describe('undoShortcutLabel', () => {
  it('says Cmd on the platforms that have one', () => {
    for (const platform of ['MacIntel', 'macOS', 'iPhone', 'iPad']) {
      expect(undoShortcutLabel(platform)).toBe('⌘Z');
    }
  });

  it('says Ctrl everywhere else, and where the platform is unknown', () => {
    for (const platform of ['Win32', 'Windows', 'Linux x86_64', '']) {
      expect(undoShortcutLabel(platform)).toBe('Ctrl+Z');
    }
  });
});
