import { describe, expect, it } from 'vitest';
import { runnerTagsDraft, runnerTagsRequest, runnerTagsSentence } from './runnerTags';

describe('runnerTagsDraft', () => {
  it('shows no tags as a blank box', () => {
    expect(runnerTagsDraft([])).toBe('');
  });

  it('shows each tag on its own line', () => {
    expect(runnerTagsDraft(['macos', 'xcode'])).toBe('macos\nxcode');
  });
});

describe('runnerTagsRequest', () => {
  it('a blank field clears the tags', () => {
    expect(runnerTagsRequest('')).toEqual([]);
  });

  it('splits the field into tags for the wire', () => {
    expect(runnerTagsRequest('macos\nxcode')).toEqual(['macos', 'xcode']);
  });

  it('drops blank lines and trims the rest', () => {
    expect(runnerTagsRequest('macos\n\n  xcode  \n')).toEqual(['macos', 'xcode']);
  });
});

describe('runnerTagsSentence', () => {
  it('reads no tags as "Any runner."', () => {
    expect(runnerTagsSentence([])).toBe('Any runner.');
  });

  it('reads one tag with no join word', () => {
    expect(runnerTagsSentence(['macos'])).toBe('Runners tagged macos.');
  });

  it('reads two tags joined by "and" with no comma', () => {
    expect(runnerTagsSentence(['macos', 'xcode'])).toBe('Runners tagged macos and xcode.');
  });

  it('reads three or more tags with commas and a final "and"', () => {
    expect(runnerTagsSentence(['macos', 'xcode', 'windows'])).toBe('Runners tagged macos, xcode and windows.');
  });
});
