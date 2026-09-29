import { describe, expect, it } from 'vitest';
import { describe as describeEvent, short } from './events';
import type { IssueEvent } from '../types';

const event = (over: Partial<IssueEvent> = {}): IssueEvent => ({
  id: 1,
  actor: 'Nathan',
  kind: 'status_changed',
  payload: { from: 'Backlog', to: 'To Do' },
  at: '2026-09-28T00:00:00Z',
  ...over,
});

describe('describe', () => {
  it('marks a hop with express, after the ordinary from/to', () => {
    expect(
      describeEvent(
        event({ kind: 'status_changed', payload: { from: 'Backlog', to: 'To Do', express: true } }),
      ),
    ).toBe('Backlog → To Do, express');
  });

  it('marks an issue filed under an express parent, naming the parent', () => {
    expect(
      describeEvent(
        event({
          kind: 'created',
          payload: { type: 'story', title: 'A story', expressFrom: 'HA-45' },
        }),
      ),
    ).toBe('express, from HA-45');
  });

  it('leaves a plain status_changed unchanged', () => {
    expect(describeEvent(event({ kind: 'status_changed', payload: { from: 'Backlog', to: 'To Do' } }))).toBe(
      'Backlog → To Do',
    );
  });

  it('leaves a plain created unchanged', () => {
    expect(describeEvent(event({ kind: 'created', payload: { type: 'story', title: 'A story' } }))).toBe('');
  });

  it('draws an override as the WIP limit it crossed', () => {
    expect(
      describeEvent(event({ kind: 'wip_overridden', payload: { limit: 5, load: 6, to: 'In Progress' } })),
    ).toBe('overrode the WIP limit — 6 of 5 into In Progress');
  });
});

describe('short', () => {
  it('reads null and undefined as none', () => {
    expect(short(null)).toBe('none');
    expect(short(undefined)).toBe('none');
  });

  it('cuts a long value rather than wrapping it', () => {
    const long = 'x'.repeat(80);
    expect(short(long)).toBe(`${'x'.repeat(60)}…`);
  });
});
