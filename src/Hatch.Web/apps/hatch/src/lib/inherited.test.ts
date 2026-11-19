import { describe, expect, it } from 'vitest';
import { inheritedMark, inheritedSentences, levelSentence, pullSentence, type InheritedFields } from './inherited';

const none: InheritedFields = {
  preApproval: 'none',
  preApprovalFrom: null,
  preApprovalPassedOver: null,
  preApprovalPassedOverLevel: null,
  priority: 'normal',
  priorityFrom: null,
  priorityPassedOver: null,
  priorityPassedOverLevel: null,
  pullPassedOver: null,
};

describe('levelSentence', () => {
  it('names the issue the level is on', () => {
    expect(levelSentence('pre-approved', 'AER-1', true, 'OPS')).toBe('pre-approved on AER-1');
    expect(levelSentence('expedited', 'AER-1', true, 'OPS')).toBe('expedited on AER-1');
  });

  it('says not honoured, with the project, for a level that was passed over', () => {
    expect(levelSentence('pre-approved', 'AER-1', false, 'OPS')).toBe('pre-approved on AER-1, not honoured in OPS');
    expect(levelSentence('express', 'AER-1', false, 'OPS')).toBe('express on AER-1, not honoured in OPS');
    expect(levelSentence('expedited', 'AER-1', false, 'OPS')).toBe('expedited on AER-1, not honoured in OPS');
  });

  it('says outside your scope where there is no key, and still says not honoured when it was passed over', () => {
    expect(levelSentence('express', null, true, 'OPS')).toBe('express from an issue outside your scope');
    expect(levelSentence('express', null, false, 'OPS')).toBe(
      'express from an issue outside your scope, not honoured in OPS',
    );
  });
});

describe('pullSentence', () => {
  it('names the parent and the project', () => {
    expect(pullSentence('AER-1', 'OPS')).toBe('AER-1 is in progress, but its pull is not honoured in OPS');
  });

  it('says outside your scope where the parent is masked', () => {
    expect(pullSentence(null, 'OPS')).toBe('from an issue outside your scope, not honoured in OPS');
  });
});

describe('inheritedSentences', () => {
  it('is all null for an item that inherits nothing', () => {
    expect(inheritedSentences(none, 'OPS')).toEqual({ preApproval: null, priority: null, pull: null });
  });

  it('says an honoured level on its ancestor', () => {
    const got = inheritedSentences(
      { ...none, preApproval: 'preapproved', preApprovalFrom: 'AER-1', priority: 'expedited', priorityFrom: 'AER-1' },
      'OPS',
    );
    expect(got.preApproval).toBe('pre-approved on AER-1');
    expect(got.priority).toBe('expedited on AER-1');
  });

  it('says an honoured level from an ancestor the reader cannot see', () => {
    const got = inheritedSentences(
      { ...none, preApproval: 'express', priority: 'emergency', fromBeyondScope: ['preApproval', 'priority'] },
      'OPS',
    );
    expect(got.preApproval).toBe('express from an issue outside your scope');
    expect(got.priority).toBe('emergency from an issue outside your scope');
  });

  it('says a level that was passed over, which the effective level no longer shows', () => {
    const got = inheritedSentences(
      {
        ...none,
        preApprovalPassedOver: 'AER-1',
        preApprovalPassedOverLevel: 'preapproved',
        priorityPassedOver: 'AER-1',
        priorityPassedOverLevel: 'expedited',
      },
      'OPS',
    );
    expect(got.preApproval).toBe('pre-approved on AER-1, not honoured in OPS');
    expect(got.priority).toBe('expedited on AER-1, not honoured in OPS');
  });

  it('keeps the verdict when the passed-over ancestor is masked', () => {
    const got = inheritedSentences(
      {
        ...none,
        preApprovalPassedOverLevel: 'express',
        priorityPassedOverLevel: 'expedited',
        fromBeyondScope: ['preApprovalPassedOver', 'priorityPassedOver', 'pullPassedOver'],
      },
      'OPS',
    );
    expect(got.preApproval).toBe('express from an issue outside your scope, not honoured in OPS');
    expect(got.priority).toBe('expedited from an issue outside your scope, not honoured in OPS');
    expect(got.pull).toBe('from an issue outside your scope, not honoured in OPS');
  });

  it('joins a passed-over level and a farther honoured one', () => {
    const got = inheritedSentences(
      {
        ...none,
        preApproval: 'express',
        preApprovalFrom: 'AER-0',
        preApprovalPassedOver: 'AER-1',
        preApprovalPassedOverLevel: 'preapproved',
        priority: 'expedited',
        priorityFrom: 'AER-0',
        priorityPassedOver: 'AER-1',
        priorityPassedOverLevel: 'emergency',
      },
      'OPS',
    );
    expect(got.preApproval).toBe('pre-approved on AER-1, not honoured in OPS; express on AER-0');
    expect(got.priority).toBe('emergency on AER-1, not honoured in OPS; expedited on AER-0');
  });

  it('says a pull that is not honoured', () => {
    expect(inheritedSentences({ ...none, pullPassedOver: 'AER-1' }, 'OPS').pull).toBe(
      'AER-1 is in progress, but its pull is not honoured in OPS',
    );
  });

  it("says nothing for a flagged value that is the item's own level", () => {
    expect(inheritedSentences({ ...none, fromBeyondScope: ['preApproval'] }, 'OPS').preApproval).toBeNull();
  });
});

describe('inheritedMark', () => {
  it('is null where nothing is said', () => {
    expect(inheritedMark(none, 'OPS')).toBeNull();
  });

  it('wears the passed-over level glyph and joins every sentence into the title', () => {
    const got = inheritedMark(
      {
        ...none,
        preApprovalPassedOver: 'AER-1',
        preApprovalPassedOverLevel: 'preapproved',
        priority: 'expedited',
        priorityFrom: 'AER-0',
        pullPassedOver: 'AER-1',
      },
      'OPS',
    );
    expect(got).toEqual({
      glyph: '✓ ↑ ↳',
      title:
        'pre-approved on AER-1, not honoured in OPS · expedited on AER-0 · AER-1 is in progress, but its pull is not honoured in OPS',
    });
  });
});
