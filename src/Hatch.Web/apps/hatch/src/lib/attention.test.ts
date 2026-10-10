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
  resetWords,
  resumeWords,
  reviewEmptyWords,
  trunkBuildEmptyWords,
  trunkBuildHead,
  trunkIconTone,
  trunkIconWords,
  waitedWords,
} from './attention';
import type { Attention, Conflict, ExhaustedRunner, Question, Review, TrunkBuild } from '../types';

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

const exhaustedRunner = (over: Partial<ExhaustedRunner> = {}): ExhaustedRunner => ({
  name: 'here:/checkouts/one',
  where: 'here:/checkouts/one',
  exhaustedUntil: new Date(NOW.getTime() + 3_600_000).toISOString(),
  ...over,
});

const trunkBuild = (over: Partial<TrunkBuild> = {}): TrunkBuild => ({
  id: 1,
  remote: 'https://forge.example/owner/repo.git',
  canonical: 'forge.example/owner/repo',
  trunk: 'main',
  sha: '1111111111111111111111111111111111111111',
  shaSince: ago(60_000),
  verdict: 'failed',
  failing: [{ name: 'CI', url: null }],
  checkedAt: ago(0),
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  bugIssueKey: null,
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

  it('does not count a runner out of Claude usage, and stays resting for it', () => {
    // A dot, not a pill: see the control's own remarks.
    const exhausted = attention({ exhaustedRunners: [exhaustedRunner()] });

    expect(attentionCount(exhausted)).toBe(0);
    expect(attentionTone(exhausted)).toBe('rest');
  });

  it('counts a failing trunk build, unlike a branch conflict or a failing branch build', () => {
    // No agent owns a trunk, so a failing one has nobody already on it - the
    // one build-shaped thing in this panel that does light the control.
    expect(attentionCount(attention({ trunkBuilds: [trunkBuild(), trunkBuild({ id: 2 })] }))).toBe(2);
  });

  it('reads an absent trunkBuilds as none, for a board that predates it', () => {
    expect(attentionCount(attention({ trunkBuilds: undefined }))).toBe(0);
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

  it('asks as soon as one trunk build is failing', () => {
    expect(attentionTone(attention({ trunkBuilds: [trunkBuild()] }))).toBe('asking');
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

  it('names a failing trunk build first, pluralised on its own count', () => {
    expect(attentionLabel(attention({ trunkBuilds: [trunkBuild()] }))).toBe('1 trunk build failing');
    expect(attentionLabel(attention({ trunkBuilds: [trunkBuild(), trunkBuild({ id: 2 })] }))).toBe(
      '2 trunk builds failing',
    );
  });

  it('combines all three parts, trunk builds first', () => {
    expect(
      attentionLabel(attention({ trunkBuilds: [trunkBuild()], reviews: [review()], questions: [question()] })),
    ).toBe('1 trunk build failing, 1 pull request to review, 1 question to answer');
  });

  it('adds a runner out of Claude usage as its own phrase, singular and plural', () => {
    expect(attentionLabel(attention({ exhaustedRunners: [exhaustedRunner()] }))).toBe(
      '1 runner is out of Claude usage',
    );
    expect(attentionLabel(attention({ exhaustedRunners: [exhaustedRunner(), exhaustedRunner()] }))).toBe(
      '2 runners are out of Claude usage',
    );
  });

  it('joins it onto the human halves rather than replacing them', () => {
    expect(attentionLabel(attention({ reviews: [review()], exhaustedRunners: [exhaustedRunner()] }))).toBe(
      '1 pull request to review, 1 runner is out of Claude usage',
    );
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

describe('trunkBuildEmptyWords', () => {
  it('says no trunk build is failing', () => {
    expect(trunkBuildEmptyWords()).toBe('No trunk build is failing.');
  });
});

describe('trunkBuildHead', () => {
  it('names the trunk as the runner reported it, and the repository', () => {
    expect(trunkBuildHead(trunkBuild())).toBe('main in forge.example/owner/repo');
  });

  it('never hardcodes a trunk name', () => {
    expect(trunkBuildHead(trunkBuild({ trunk: 'trunk', canonical: 'example.test/o/r' }))).toBe('trunk in example.test/o/r');
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

describe('resumeWords', () => {
  const STALL_RESUME_SECONDS = 900;

  it('counts down the minutes left in the shipped window', () => {
    expect(resumeWords(ago(12 * 60_000), STALL_RESUME_SECONDS, NOW)).toBe('in 3 minutes');
  });

  it('says any moment now once the window has passed', () => {
    expect(resumeWords(ago(16 * 60_000), STALL_RESUME_SECONDS, NOW)).toBe('any moment now');
  });

  it('says it will not resume on its own when the install has turned the window off', () => {
    expect(resumeWords(ago(60_000), 0, NOW)).toBe('will not resume on its own');
  });

  it('falls back to a word rather than NaN on an unparseable instant', () => {
    expect(resumeWords('not a date', STALL_RESUME_SECONDS, NOW)).toBe('resumes on its own');
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

describe('resetWords', () => {
  it('names just the clock for later today', () => {
    const soon = new Date(NOW.getTime() + 60_000).toISOString();
    const words = resetWords(soon, NOW);

    expect(words).toMatch(/^resets \d{1,2}:\d{2}/);
    expect(words).not.toContain(' at ');
  });

  it('names the weekday within the week', () => {
    const within = new Date(NOW.getTime() + 3 * 86_400_000).toISOString();
    expect(resetWords(within, NOW)).toMatch(/^resets [A-Za-z]+ at \d{1,2}:\d{2}/);
  });

  it('names the date beyond a week', () => {
    const later = new Date(NOW.getTime() + 30 * 86_400_000).toISOString();
    expect(resetWords(later, NOW)).toMatch(/^resets [A-Za-z]{3} \d{1,2} at \d{1,2}:\d{2}/);
  });

  it('is unknown for a time that cannot be read', () => {
    expect(resetWords('not a date', NOW)).toBe('resets at an unknown time');
  });
});
