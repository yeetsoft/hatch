import { describe, expect, it } from 'vitest';
import {
  attentionCount,
  attentionLabel,
  attentionTone,
  buildIconTone,
  buildIconWords,
  conflictEmptyWords,
  failingBuildEmptyWords,
  questionEmptyWords,
  reviewEmptyWords,
  trunkIconTone,
  trunkIconWords,
  waitedWords,
} from './attention';
import type { Attention, Conflict, Question, Review } from '../types';

const NOW = new Date('2026-09-09T12:00:00Z');

const ago = (ms: number) => new Date(NOW.getTime() - ms).toISOString();

const review = (over: Partial<Review> = {}): Review => ({
  key: 'AER-12',
  title: 'A story that landed',
  type: 'story',
  pullRequestUrl: 'https://forge.example/pulls/12',
  buildState: 'unknown',
  holdsTrunk: null,
  trunk: null,
  ...over,
});

const question = (over: Partial<Question> = {}): Question => ({
  id: 1,
  issueKey: 'AER-13',
  issueTitle: 'A story that stopped',
  body: 'How should drain retries be scoped?',
  askedBy: 'hatch',
  askedAt: ago(60 * 60_000),
  options: null,
  answers: [],
  ...over,
});

const conflict = (over: Partial<Conflict> = {}): Conflict => ({
  key: 'AER-14',
  title: 'A story whose branch stopped merging',
  type: 'story',
  pullRequestUrl: 'https://forge.example/pulls/14',
  checks: [],
  ...over,
});

const attention = (over: Partial<Attention> = {}): Attention => ({
  reviews: [],
  inReviewWithoutPullRequest: 0,
  questions: [],
  conflicts: [],
  reviewsHeldBack: 0,
  ...over,
});

describe('attentionCount', () => {
  it('counts the rows the panel would draw', () => {
    expect(attentionCount(attention({ reviews: [review(), review()], questions: [question()] }))).toBe(3);
  });

  it('does not count an issue in review with no pull request', () => {
    // The whole reason the control is still readable after a week: a ticket
    // nobody can act on from here never makes it loud.
    expect(attentionCount(attention({ inReviewWithoutPullRequest: 9 }))).toBe(0);
  });

  it('is zero before the first answer', () => {
    expect(attentionCount(null)).toBe(0);
  });

  it('does not count a branch that conflicts', () => {
    // The loop's to fix, and one it cannot fix becomes a stall - which is a
    // question, which is counted already.
    expect(attentionCount(attention({ conflicts: [conflict(), conflict({ key: 'AER-15' })] }))).toBe(0);
  });

  it('does not count a build that is failing, and stays resting for it', () => {
    // The loop fixes it, and one it cannot fix becomes a question.
    const failing = attention({
      failingBuilds: [{ key: 'AER-16', title: 'red', type: 'story', pullRequestUrl: null, checks: [] }],
    });

    expect(attentionCount(failing)).toBe(0);
    expect(attentionTone(failing)).toBe('rest');
    expect(attentionLabel(failing)).toBe('Nothing is waiting on you');
  });

  it('ignores a held-back pull request even alongside reviews that are not', () => {
    // reviewsHeldBack is not a second source for this count - only
    // `reviews.length` is, however inconsistent a caller's data might be.
    expect(attentionCount(attention({ reviews: [], reviewsHeldBack: 3 }))).toBe(0);
    expect(attentionCount(attention({ reviews: [review()], reviewsHeldBack: 3 }))).toBe(1);
  });
});

describe('attentionTone', () => {
  it('rests when nothing is waiting', () => {
    expect(attentionTone(attention())).toBe('rest');
  });

  it('rests before the first answer', () => {
    // Not a third state. "We have not asked yet" and "nothing is waiting" are
    // the same thing to draw.
    expect(attentionTone(null)).toBe('rest');
  });

  it('asks as soon as one row exists', () => {
    expect(attentionTone(attention({ questions: [question()] }))).toBe('asking');
  });

  it('stays resting for review issues that carry no pull request', () => {
    expect(attentionTone(attention({ inReviewWithoutPullRequest: 4 }))).toBe('rest');
  });

  it('stays resting for a branch that conflicts', () => {
    expect(attentionTone(attention({ conflicts: [conflict()] }))).toBe('rest');
  });
});

describe('attentionLabel', () => {
  it('names both halves', () => {
    expect(attentionLabel(attention({ reviews: [review(), review()], questions: [question()] }))).toBe(
      '2 pull requests to review, 1 question to answer',
    );
  });

  it('drops the half that is empty', () => {
    expect(attentionLabel(attention({ reviews: [review()] }))).toBe('1 pull request to review');
    expect(attentionLabel(attention({ questions: [question(), question()] }))).toBe('2 questions to answer');
  });

  it('pluralises each half on its own count', () => {
    expect(attentionLabel(attention({ reviews: [review()], questions: [question(), question()] }))).toBe(
      '1 pull request to review, 2 questions to answer',
    );
  });

  it('says so when nothing is waiting', () => {
    expect(attentionLabel(attention())).toBe('Nothing is waiting on you');
    expect(attentionLabel(null)).toBe('Nothing is waiting on you');
  });

  it('says nothing is waiting when the only thing that has happened is a conflict', () => {
    expect(attentionLabel(attention({ conflicts: [conflict()] }))).toBe('Nothing is waiting on you');
  });

  it('says nothing is waiting when the only thing in review has no pull request', () => {
    expect(attentionLabel(attention({ inReviewWithoutPullRequest: 3 }))).toBe('Nothing is waiting on you');
  });
});

describe('reviewEmptyWords', () => {
  it('says the board is quiet when nothing is in review at all', () => {
    expect(reviewEmptyWords(attention())).toBe('Nothing is up for review.');
    expect(reviewEmptyWords(null)).toBe('Nothing is up for review.');
  });

  it('says how many are in review with nowhere to review them', () => {
    // The other emptiness: a ticket whose agent forgot `hatch pr`, visible
    // here and nowhere else.
    expect(reviewEmptyWords(attention({ inReviewWithoutPullRequest: 3 }))).toBe(
      '3 issues are in review with no pull request recorded.',
    );
  });

  it('says one of them in the singular', () => {
    expect(reviewEmptyWords(attention({ inReviewWithoutPullRequest: 1 }))).toBe(
      '1 issue is in review with no pull request recorded.',
    );
  });

  it('says how many pull requests are waiting on the loop', () => {
    // The third emptiness: every pull request in review is held back by a
    // conflict or a failed build, so none of them is this section's to show.
    expect(reviewEmptyWords(attention({ reviewsHeldBack: 2 }))).toBe('2 pull requests are waiting on the loop.');
  });

  it('says one held-back pull request in the singular', () => {
    expect(reviewEmptyWords(attention({ reviewsHeldBack: 1 }))).toBe('1 pull request is waiting on the loop.');
  });

  it('combines with the no-pull-request wording when both are nonzero', () => {
    expect(reviewEmptyWords(attention({ inReviewWithoutPullRequest: 2, reviewsHeldBack: 1 }))).toBe(
      '2 issues are in review with no pull request recorded. 1 pull request is waiting on the loop.',
    );
  });
});

describe('failingBuildEmptyWords', () => {
  it('says no build is failing', () => {
    expect(failingBuildEmptyWords()).toBe('No build in review is failing.');
  });
});

describe('conflictEmptyWords', () => {
  it('says no branch has stopped merging', () => {
    expect(conflictEmptyWords()).toBe('No branch in review has stopped merging.');
  });
});

describe('questionEmptyWords', () => {
  it('says nothing is waiting on an answer', () => {
    expect(questionEmptyWords()).toBe('Nothing is waiting on an answer.');
  });
});

describe('waitedWords', () => {
  it('says just asked under a minute', () => {
    expect(waitedWords(ago(30_000), NOW)).toBe('just asked');
  });

  it('counts minutes, then hours, then days', () => {
    expect(waitedWords(ago(4 * 60_000), NOW)).toBe('4 minutes');
    expect(waitedWords(ago(3 * 60 * 60_000), NOW)).toBe('3 hours');
    expect(waitedWords(ago(50 * 60 * 60_000), NOW)).toBe('2 days');
  });

  it('reaches days, which a question over a weekend needs', () => {
    expect(waitedWords(ago(3 * 24 * 60 * 60_000), NOW)).toBe('3 days');
  });

  it('says each unit in the singular at one', () => {
    expect(waitedWords(ago(60_000), NOW)).toBe('1 minute');
    expect(waitedWords(ago(60 * 60_000), NOW)).toBe('1 hour');
    expect(waitedWords(ago(24 * 60 * 60_000), NOW)).toBe('1 day');
  });

  it('does not go backwards on a clock that is behind the server', () => {
    expect(waitedWords(new Date(NOW.getTime() + 60_000).toISOString(), NOW)).toBe('just asked');
  });

  it('falls back to a word rather than NaN on an unparseable instant', () => {
    expect(waitedWords('not a date', NOW)).toBe('waiting');
  });
});

describe('buildIconWords', () => {
  it('says the build passed', () => {
    expect(buildIconWords('success')).toBe('Build passed');
  });

  it('says the build failed - drawn and pinned even though no real row ever carries it', () => {
    expect(buildIconWords('failure')).toBe('Build failed');
  });

  it('says no results for anything else', () => {
    expect(buildIconWords('unknown')).toBe('No build results yet');
  });
});

describe('buildIconTone', () => {
  it('is success, danger or muted', () => {
    expect(buildIconTone('success')).toBe('success');
    expect(buildIconTone('failure')).toBe('danger');
    expect(buildIconTone('unknown')).toBe('muted');
  });
});

describe('trunkIconWords', () => {
  it('names the trunk as the runner reported it when up to date', () => {
    expect(trunkIconWords(true, 'trunk')).toBe('Up to date with trunk');
  });

  it('names the trunk when behind', () => {
    expect(trunkIconWords(false, 'trunk')).toBe('Behind trunk');
  });

  it('says not checked when no runner has said', () => {
    expect(trunkIconWords(null, null)).toBe('Not checked against the trunk yet');
  });
});

describe('trunkIconTone', () => {
  it('is success only when the branch holds the trunk', () => {
    expect(trunkIconTone(true)).toBe('success');
  });

  it('is muted when behind, and muted when not checked - the words tell those apart', () => {
    expect(trunkIconTone(false)).toBe('muted');
    expect(trunkIconTone(null)).toBe('muted');
  });
});
