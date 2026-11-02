import { describe, expect, it } from 'vitest';
import { outOfDateProposalCount, promptChanged, proposalFieldRows, proposalsBannerSentence } from './playbookProposals';
import type { Playbook, PlaybookProposal, PlaybookProposalChange } from '../types';

const change = (outOfDate: boolean): PlaybookProposalChange => ({
  id: 1,
  playbookId: 1,
  before: {} as Playbook,
  after: {} as Playbook,
  changedFields: [],
  outOfDate,
  outOfDateReason: outOfDate ? 'the row changed since this was proposed' : null,
});

const proposal = (changes: PlaybookProposalChange[]): PlaybookProposal => ({
  id: 1,
  reason: 'why',
  issueKey: null,
  author: 'someone',
  state: 'pending',
  createdAt: '2026-01-01T00:00:00Z',
  decidedAt: null,
  decidedBy: null,
  changes,
});

describe('outOfDateProposalCount', () => {
  it('is zero when none are out of date', () => {
    expect(outOfDateProposalCount([proposal([change(false)]), proposal([change(false)])])).toBe(0);
  });

  it('counts one of several', () => {
    expect(outOfDateProposalCount([proposal([change(true)]), proposal([change(false)])])).toBe(1);
  });

  it('counts a proposal once even when two of its changes are out of date', () => {
    expect(outOfDateProposalCount([proposal([change(true), change(true)])])).toBe(1);
  });
});

describe('proposalsBannerSentence', () => {
  it('does not throw for zero proposals', () => {
    expect(proposalsBannerSentence([])).toBe('0 proposed changes awaiting a decision');
  });

  it('says change, singular, for one', () => {
    expect(proposalsBannerSentence([proposal([change(false)])])).toBe('1 proposed change awaiting a decision');
  });

  it('says changes, plural, for several', () => {
    expect(proposalsBannerSentence([proposal([change(false)]), proposal([change(false)])])).toBe(
      '2 proposed changes awaiting a decision',
    );
  });

  it('appends the out-of-date count when one is out of date', () => {
    expect(proposalsBannerSentence([proposal([change(true)]), proposal([change(false)])])).toBe(
      '2 proposed changes awaiting a decision, 1 out of date',
    );
  });

  it('appends the out-of-date count when more than one is out of date', () => {
    expect(
      proposalsBannerSentence([proposal([change(true)]), proposal([change(true)]), proposal([change(false)])]),
    ).toBe('3 proposed changes awaiting a decision, 2 out of date');
  });
});

describe('proposalFieldRows', () => {
  const FIELDS = [
    'model',
    'effort',
    'fromStatusId',
    'toStatusId',
    'types',
    'shape',
    'budget',
    'briefLimit',
    'deniedTools',
    'isCheck',
    'projectId',
  ];

  it('comes back as the exact eleven rows, in order, unchanged for an empty changedFields', () => {
    expect(proposalFieldRows([]).map((r) => r.field)).toEqual(FIELDS);
    expect(proposalFieldRows([]).every((r) => r.changed === false)).toBe(true);
  });

  it('flips a field to changed when it is named', () => {
    for (const field of FIELDS) {
      expect(proposalFieldRows([field]).find((r) => r.field === field)?.changed).toBe(true);
    }
  });

  it('never includes prompt, even when it is in changedFields', () => {
    expect(proposalFieldRows(['prompt']).some((r) => r.field === 'prompt')).toBe(false);
  });
});

describe('promptChanged', () => {
  it('is true when prompt is in changedFields', () => {
    expect(promptChanged(['prompt'])).toBe(true);
  });

  it('is false when it is not', () => {
    expect(promptChanged(['model'])).toBe(false);
  });
});
