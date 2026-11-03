import { describe, expect, it } from 'vitest';
import {
  approveAllDisabledHint,
  approveAllSuccessSentence,
  outOfDateProposalCount,
  promptChanged,
  proposalFieldRows,
  proposalsBannerSentence,
  rejectAllSuccessSentence,
} from './playbookProposals';
import type { Playbook, PlaybookProposal, PlaybookProposalChange } from '../types';

const change = (outOfDate: boolean, before: Partial<Playbook> = {}): PlaybookProposalChange => ({
  id: 1,
  playbookId: 1,
  before: before as Playbook,
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

const TO_DO_TO_IN_PROGRESS = { fromStatusId: 1, toStatusId: 2, fromStatusName: 'To Do', toStatusName: 'In Progress' };
const IN_PROGRESS_TO_REVIEW = { fromStatusId: 2, toStatusId: 3, fromStatusName: 'In Progress', toStatusName: 'Review' };

describe('approveAllDisabledHint', () => {
  it('is null for no proposals', () => {
    expect(approveAllDisabledHint([])).toBeNull();
  });

  it('is null when every change is not out of date', () => {
    expect(approveAllDisabledHint([proposal([change(false)]), proposal([change(false)])])).toBeNull();
  });

  it('names the out-of-date change and its reason when one is out of date', () => {
    expect(approveAllDisabledHint([proposal([change(true, TO_DO_TO_IN_PROGRESS)])])).toBe(
      'To Do to In Progress was the row changed since this was proposed - reject it or have it resubmitted',
    );
  });

  it('returns the first out-of-date change, not the second', () => {
    expect(
      approveAllDisabledHint([proposal([change(true, TO_DO_TO_IN_PROGRESS), change(true, IN_PROGRESS_TO_REVIEW)])]),
    ).toBe('To Do to In Progress was the row changed since this was proposed - reject it or have it resubmitted');
  });
});

describe('approveAllSuccessSentence', () => {
  it('says change, singular, for one', () => {
    expect(approveAllSuccessSentence(1)).toBe('1 change applied');
  });

  it('says changes, plural, for zero', () => {
    expect(approveAllSuccessSentence(0)).toBe('0 changes applied');
  });

  it('says changes, plural, for several', () => {
    expect(approveAllSuccessSentence(2)).toBe('2 changes applied');
  });
});

describe('rejectAllSuccessSentence', () => {
  it('says proposal, singular, for one', () => {
    expect(rejectAllSuccessSentence(1)).toBe('1 proposal rejected');
  });

  it('says proposals, plural, for zero', () => {
    expect(rejectAllSuccessSentence(0)).toBe('0 proposals rejected');
  });

  it('says proposals, plural, for several', () => {
    expect(rejectAllSuccessSentence(2)).toBe('2 proposals rejected');
  });
});
