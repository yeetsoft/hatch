import { describe, expect, it } from 'vitest';
import { runnerFailureAdvice, runnerFailureRowWords } from './runnerFailureRows';
import { resetClockWords } from './attention';
import type { AttentionRunnerFailure } from '../types';

const NOW = new Date('2026-09-09T12:00:00Z');
const began = new Date(NOW.getTime() - 3_600_000).toISOString();

const row = (kind: string, over: Partial<AttentionRunnerFailure> = {}): AttentionRunnerFailure => ({
  name: 'one',
  where: 'here:/checkouts/one',
  kind,
  why: null,
  beganAt: began,
  ...over,
});

describe('runnerFailureAdvice', () => {
  it('tells a person what to do about each kind, in its own words', () => {
    expect(runnerFailureAdvice('forge')).toBe('Sign gh in on that machine.');
    expect(runnerFailureAdvice('signing')).toBe('Fix signing on that machine, or allow unsigned commits on the project.');
    expect(runnerFailureAdvice('agent-signed-out')).toBe('Sign the agent in on that machine.');
    expect(runnerFailureAdvice('agent-not-starting')).toBe("Read what the agent said, on the runner's page.");
    expect(runnerFailureAdvice('board-paused')).toBe("Read why on the runner's page, where the pause can be lifted.");
  });

  it('gives five different sentences, one for each kind', () => {
    const kinds = ['forge', 'signing', 'agent-signed-out', 'agent-not-starting', 'board-paused'];
    expect(new Set(kinds.map(runnerFailureAdvice)).size).toBe(5);
  });

  it('points at the runner\'s page for a kind it does not know', () => {
    expect(runnerFailureAdvice('disk-full')).toBe("Open the runner's page to see what it cannot do.");
  });
});

describe('runnerFailureRowWords', () => {
  const clock = resetClockWords(new Date(began), NOW);

  it('says what the runner cannot do and since when, in the viewer\'s clock', () => {
    expect(runnerFailureRowWords(row('forge'), NOW)).toBe(`Cannot reach its forge since ${clock}`);
    expect(runnerFailureRowWords(row('board-paused'), NOW)).toBe(`Paused by the board since ${clock}`);
  });

  it('adds what the tool said, and who said it', () => {
    expect(runnerFailureRowWords(row('forge', { why: 'token expired' }), NOW)).toBe(
      `Cannot reach its forge since ${clock} - gh said: token expired`,
    );
    expect(runnerFailureRowWords(row('agent-signed-out', { why: 'please log in' }), NOW)).toBe(
      `Its agent is signed out since ${clock} - agent said: please log in`,
    );
  });

  it('leaves the checkout to the row\'s head', () => {
    expect(runnerFailureRowWords(row('signing'), NOW)).not.toContain('checkouts');
  });
});
