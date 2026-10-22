import { describe, expect, it } from 'vitest';
import { joinTags, runnerTagsDraft, runnerTagsFromLabel, runnerTagsRequest, runnerTagsSentence } from './runnerTags';
import type { RunnerTagSource } from '../types';

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

describe('joinTags', () => {
  it('joins zero tags as nothing', () => {
    expect(joinTags([])).toBe('');
  });

  it('joins one tag with no join word', () => {
    expect(joinTags(['macos'])).toBe('macos');
  });

  it('joins two tags with "and" and no comma', () => {
    expect(joinTags(['macos', 'xcode'])).toBe('macos and xcode');
  });

  it('joins three or more tags with commas and a final "and"', () => {
    expect(joinTags(['macos', 'xcode', 'windows'])).toBe('macos, xcode and windows');
  });
});

describe('runnerTagsFromLabel', () => {
  it('names a project source "the project"', () => {
    const source: RunnerTagSource = { kind: 'project', key: 'AER', tags: ['macos'] };
    expect(runnerTagsFromLabel(source)).toBe('the project');
  });

  it('names an issue source by its key', () => {
    const source: RunnerTagSource = { kind: 'issue', key: 'AER-1', tags: ['macos'] };
    expect(runnerTagsFromLabel(source)).toBe('AER-1');
  });
});
