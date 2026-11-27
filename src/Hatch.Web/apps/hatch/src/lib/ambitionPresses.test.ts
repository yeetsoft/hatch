import { describe, expect, it } from 'vitest';
import { pressFor, startSentences } from './ambitionPresses';
import type { AmbitionRow, Me } from '../types';

const row = (over: Partial<AmbitionRow> = {}): AmbitionRow => ({
  key: 'AER-1',
  projectKey: 'AER',
  title: 'an ambition',
  statusId: 1,
  statusName: 'To Do',
  stage: 'draft',
  stageWhy: 'It has no capabilities yet.',
  limit: 1,
  capabilities: 0,
  holds: 0,
  inFlight: 0,
  waitsOnPerson: 0,
  notStarted: 0,
  newlyHolding: 0,
  open: [],
  summary: '',
  decisions: 0,
  budget: { budgetUsd: null, spentUsd: 0, unpricedSessions: 0, spent: false },
  childBeyondScope: false,
  shapingStatusId: 2,
  shapingStatusName: 'Idea',
  startStatusId: 3,
  startStatusName: 'Doing',
  ...over,
});

const meOf = (over: Partial<Me> = {}): Me => ({
  kind: 'person',
  name: '',
  role: 'user',
  configured: true,
  canSignOut: true,
  canSignIn: false,
  ...over,
});

const person = meOf();
const key = meOf({ kind: 'key' });

describe('pressFor', () => {
  it('offers Shape it for a draft whose project has a shaping row', () => {
    expect(pressFor(row({ stage: 'draft', shapingStatusId: 2 }), person)).toBe('shape');
  });

  it('offers nothing for a draft whose project has no shaping row', () => {
    expect(pressFor(row({ stage: 'draft', shapingStatusId: null }), person)).toBeNull();
  });

  it('offers Start it for a ready row with an implementation column', () => {
    expect(pressFor(row({ stage: 'ready', startStatusId: 3 }), person)).toBe('start');
  });

  it('offers nothing for a ready row on a board with no implementation column', () => {
    expect(pressFor(row({ stage: 'ready', startStatusId: null }), person)).toBeNull();
  });

  it('offers nothing for any other stage', () => {
    expect(pressFor(row({ stage: 'running' }), person)).toBeNull();
    expect(pressFor(row({ stage: 'shaping' }), person)).toBeNull();
  });

  it('offers nothing to a key, whatever the row', () => {
    expect(pressFor(row({ stage: 'draft' }), key)).toBeNull();
    expect(pressFor(row({ stage: 'ready' }), key)).toBeNull();
  });

  it('offers the press while nobody has answered who is here, same as a person', () => {
    expect(pressFor(row({ stage: 'draft' }), null)).toBe('shape');
  });
});

describe('startSentences', () => {
  it('names the capability count, singular and plural', () => {
    expect(startSentences(row({ capabilities: 1 }), 'none')[0]).toBe('A pass will check 1 capability.');
    expect(startSentences(row({ capabilities: 4 }), 'none')[0]).toBe('A pass will check 4 capabilities.');
  });

  it('names how many epics run at once, singular and plural', () => {
    expect(startSentences(row({ limit: 1 }), 'none')[1]).toBe('1 epic runs at once.');
    expect(startSentences(row({ limit: 3 }), 'none')[1]).toBe('3 epics run at once.');
  });

  it('names the budget, or that none is set', () => {
    expect(startSentences(row({ budget: { budgetUsd: null, spentUsd: 0, unpricedSessions: 0, spent: false } }), 'none')[2]).toBe(
      'No budget is set.',
    );
    expect(startSentences(row({ budget: { budgetUsd: 25, spentUsd: 4, unpricedSessions: 0, spent: false } }), 'none')[2]).toBe(
      'Budget: $25.00.',
    );
  });

  it('names the pre-approval level', () => {
    expect(startSentences(row(), 'none')[3]).toBe('Pre-approval: None.');
    expect(startSentences(row(), 'express')[3]).toBe('Pre-approval: Express.');
    expect(startSentences(row(), 'preapproved')[3]).toBe('Pre-approval: Pre-approved.');
  });
});
