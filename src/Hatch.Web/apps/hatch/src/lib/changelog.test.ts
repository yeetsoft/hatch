import { describe, expect, it } from 'vitest';
import { fixLine } from './changelog';

describe('fixLine', () => {
  it('reads the release note\'s first line', () => {
    expect(fixLine({ title: 'Title', releaseNote: 'First line.\n\nMore.' })).toBe('First line.');
  });

  it('drops a trailing \\r on a \\r\\n note', () => {
    expect(fixLine({ title: 'Title', releaseNote: 'First line.\r\nMore.' })).toBe('First line.');
  });

  it('skips leading blank lines and whitespace to the first real line', () => {
    expect(fixLine({ title: 'Title', releaseNote: '\n  \nFirst real line.\nMore.' })).toBe('First real line.');
  });

  it('falls back to the title when the note is null', () => {
    expect(fixLine({ title: 'Title', releaseNote: null })).toBe('Title');
  });

  it('falls back to the title when the note is empty', () => {
    expect(fixLine({ title: 'Title', releaseNote: '' })).toBe('Title');
  });

  it('falls back to the title when the note is only whitespace', () => {
    expect(fixLine({ title: 'Title', releaseNote: '  \n ' })).toBe('Title');
  });
});
