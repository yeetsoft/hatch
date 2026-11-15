import { describe, expect, it } from 'vitest';
import { MODE_LABEL, MODE_VERB, PREFIXES, parsePrompt } from './consolePrompt';

describe('parsePrompt', () => {
  it('reads no prefix as the issue search, untouched', () => {
    expect(parsePrompt('ha-3')).toEqual({ mode: 'issues', query: 'ha-3' });
    expect(parsePrompt('')).toEqual({ mode: 'issues', query: '' });
    expect(parsePrompt('  words ')).toEqual({
      mode: 'issues',
      query: '  words ',
    });
  });

  it('reads > as commands', () => {
    expect(parsePrompt('>go board')).toEqual({
      mode: 'commands',
      query: 'go board',
    });
  });

  it('reads a prefix on its own', () => {
    expect(parsePrompt('>')).toEqual({ mode: 'commands', query: '' });
    expect(parsePrompt('/')).toEqual({ mode: 'issues', query: '' });
    expect(parsePrompt('?')).toEqual({ mode: 'help', query: '' });
  });

  it('reads / as issues, and drops the slash', () => {
    expect(parsePrompt('/ha-3')).toEqual({ mode: 'issues', query: 'ha-3' });
  });

  it('ignores a leading space before the prefix', () => {
    expect(parsePrompt('  >board')).toEqual({
      mode: 'commands',
      query: 'board',
    });
    expect(parsePrompt(' ?')).toEqual({ mode: 'help', query: '' });
  });

  it('ignores whatever follows a ?', () => {
    expect(parsePrompt('?anything at all')).toEqual({
      mode: 'help',
      query: '',
    });
  });

  it('reads only the first character as a prefix', () => {
    expect(parsePrompt('ha>3')).toEqual({ mode: 'issues', query: 'ha>3' });
    expect(parsePrompt('>>x')).toEqual({ mode: 'commands', query: '>x' });
  });
});

describe('the mode tables', () => {
  it('lists /, > and ? in that order', () => {
    expect(PREFIXES.map((p) => p.prefix)).toEqual(['/', '>', '?']);
  });

  it('gives every mode a verb and a label', () => {
    for (const { mode } of PREFIXES) {
      expect(MODE_VERB[mode]).toBeTruthy();
      expect(MODE_LABEL[mode]).toBeTruthy();
    }
    expect(MODE_VERB.commands).toBe('run');
  });
});
