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

  it('marks a hop with parent pulled, after the ordinary from/to', () => {
    expect(
      describeEvent(
        event({ kind: 'status_changed', payload: { from: 'To Do', to: 'In Progress', pulled: true } }),
      ),
    ).toBe('To Do → In Progress, parent pulled');
  });

  it('marks a hop with epic, after the ordinary from/to', () => {
    expect(
      describeEvent(
        event({ kind: 'status_changed', payload: { from: 'To Do', to: 'In Progress', epic: true } }),
      ),
    ).toBe('To Do → In Progress, epic');
  });

  it('marks a hop under a running epic, naming it, after the ordinary from/to', () => {
    expect(
      describeEvent(
        event({ kind: 'status_changed', payload: { from: 'Backlog', to: 'To Do', under: 'HA-86' } }),
      ),
    ).toBe('Backlog → To Do, under HA-86');
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

  it('names an epic wip limit set, with the unset side read as the default', () => {
    expect(
      describeEvent(event({ kind: 'wip_limit_changed', payload: { from: null, to: 3 } })),
    ).toBe('stories at once: 1 → 3');
  });

  it('names an epic wip limit changed from one number to another', () => {
    expect(
      describeEvent(event({ kind: 'wip_limit_changed', payload: { from: 2, to: 3 } })),
    ).toBe('stories at once: 2 → 3');
  });

  it('names an epic wip limit cleared, with the unset side read as the default', () => {
    expect(
      describeEvent(event({ kind: 'wip_limit_changed', payload: { from: 3, to: null } })),
    ).toBe('stories at once: 3 → 1');
  });

  it('names the runner that stopped answering and when, for a lapsed takeover', () => {
    expect(
      describeEvent(
        event({
          kind: 'claim_lapsed',
          payload: { from: 'Buster Bluth on here:/checkouts/one', heardAt: '2026-09-28T00:00:00Z' },
        }),
      ),
    ).toBe('Buster Bluth on here:/checkouts/one stopped answering, last heard from 2026-09-28T00:00:00Z');
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
