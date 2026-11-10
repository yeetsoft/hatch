import { describe, expect, it } from 'vitest';
import {
  agentSectionRowCounts,
  approvedHeldEmptyWords,
  attentionCount,
  attentionLabel,
  attentionTone,
  buildIconTone,
  buildIconWords,
  closesOnPress,
  conflictEmptyWords,
  deadlockedEmptyWords,
  failingBuildEmptyWords,
  groupCollapsed,
  humanSectionRowCounts,
  landingHeldEmptyWords,
  noCodeHeldEmptyWords,
  pillCount,
  questionEmptyWords,
  resetWords,
  resumeWords,
  reviewEmptyWords,
  reviewWordsCarryAFact,
  runnerCapacityEmptyWords,
  runnerCapacityWords,
  sectionCollapsed,
  stalledEmptyWords,
  stalledWords,
  trainStalledWords,
  trunkBuildEmptyWords,
  trunkBuildHead,
  trunkIconTone,
  trunkIconWords,
  waitedWords,
  waitingOnARunnerEmptyWords,
} from './attention';
import type { ApprovedHeld, ApprovedWaiting, Attention, BackupAttention, Conflict, Deadlocked, ExhaustedRunner, LandingHeld, NoCodeHeld, Question, Review, RunnerCapacity, StalledIssue, TrainStalled, TrunkBuild, WaitingOnARunner } from '../types';

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
  playbookFilePath: null,
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

const approvedHeld = (over: Partial<ApprovedHeld> = {}): ApprovedHeld => ({
  key: 'AER-17',
  title: 'A story the merge column cannot land',
  type: 'story',
  pullRequestUrl: 'https://forge.example/pulls/17',
  sentence: 'Conflicts with main.',
  ...over,
});

const noCodeHeld = (over: Partial<NoCodeHeld> = {}): NoCodeHeld => ({
  key: 'AER-18',
  title: 'A task that needed no code',
  type: 'task',
  reason: 'The setting was already off.',
  ...over,
});

const landingHeld = (over: Partial<LandingHeld> = {}): LandingHeld => ({
  key: 'AER-19',
  title: 'A story that landed and is not done',
  type: 'story',
  sentence: 'Its pull request merged but the issue is not done.',
  ...over,
});

const deadlocked = (over: Partial<Deadlocked> = {}): Deadlocked => ({
  key: 'AER-21',
  title: 'A story stuck in a ring',
  type: 'story',
  sentence: 'deadlocked - it waits on AER-22, which waits on it.',
  ...over,
});

const waitingOnARunner = (over: Partial<WaitingOnARunner> = {}): WaitingOnARunner => ({
  key: 'AER-24',
  title: 'A story that runs on fable',
  type: 'story',
  move: 'todo',
  missing: 'no live runner offers fable',
  ...over,
});

const runnerCapacity = (over: Partial<RunnerCapacity> = {}): RunnerCapacity => ({
  projectKey: 'AER',
  steps: 2,
  keys: ['AER-30', 'AER-31'],
  takers: [],
  reasons: [{ runner: 'Nathan-PC', reason: 'out of usage' }],
  ...over,
});

const approvedWaiting = (over: Partial<ApprovedWaiting> = {}): ApprovedWaiting => ({
  key: 'AER-18',
  title: 'A story the loop is still merging',
  type: 'story',
  pullRequestUrl: 'https://forge.example/pulls/18',
  kind: 'merge',
  sentence: 'Merging.',
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

const trainStalled = (over: Partial<TrainStalled> = {}): TrainStalled => ({
  projectKey: 'AER',
  projectName: 'Aerie',
  passedOverRunner: 'laptop:/work/aerie',
  electedRunner: 'desk:/work/aerie',
  headKey: 'AER-31',
  at: '2026-09-09T12:00:00Z',
  ...over,
});

const stalledIssue = (over: Partial<StalledIssue> = {}): StalledIssue => ({
  key: 'AER-26',
  title: 'A story nobody has touched',
  type: 'story',
  stalledAt: ago(60_000),
  stalledWhy: 'Left where it was found.',
  held: false,
  letGo: 1,
  sentence: 'The loop let it go once.',
  kind: 'loop',
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

  it('counts an approvedHeld row and excludes an approvedWaiting row on the same fixture', () => {
    // approvedHeld is a person's to fix, every time; approvedWaiting is the
    // loop's own and does not light the badge.
    expect(
      attentionCount(attention({ approvedHeld: [approvedHeld()], approvedWaiting: [approvedWaiting()] })),
    ).toBe(1);
  });

  it('counts a noCodeHeld row, adds it to the other sources, and counts an absent field as zero', () => {
    expect(attentionCount(attention({}))).toBe(0);
    expect(attentionCount(attention({ noCodeHeld: [noCodeHeld()] }))).toBe(1);
    expect(attentionCount(attention({ reviews: [review()], noCodeHeld: [noCodeHeld()] }))).toBe(2);
  });

  it('counts a landingHeld row, adds it to the other sources, and counts an absent field as zero', () => {
    // Nothing dispatches at an issue whose work already reached the trunk, so
    // every row is a person's to finish.
    expect(attentionCount(attention({ landingHeld: [landingHeld(), landingHeld({ key: 'AER-20' })] }))).toBe(2);
    expect(attentionCount(attention({ reviews: [review()], landingHeld: [landingHeld()] }))).toBe(2);
    expect(attentionCount(attention())).toBe(0);
  });

  it('counts a deadlocked row, adds it to the other sources, and counts an absent field as zero', () => {
    // Nothing dispatches into a ring, so every row is a person's to break.
    expect(attentionCount(attention({ deadlocked: [deadlocked(), deadlocked({ key: 'AER-23' })] }))).toBe(2);
    expect(attentionCount(attention({ reviews: [review()], deadlocked: [deadlocked()] }))).toBe(2);
    expect(attentionCount(attention())).toBe(0);
  });

  it('counts a waiting-on-a-runner row, adds it to the other sources, and counts an absent field as zero', () => {
    // No loop is on a move no live runner can take, so every row is a person's to fix.
    expect(attentionCount(attention({ waitingOnARunner: [waitingOnARunner(), waitingOnARunner({ key: 'AER-25' })] }))).toBe(2);
    expect(attentionCount(attention({ reviews: [review()], waitingOnARunner: [waitingOnARunner()] }))).toBe(2);
    expect(attentionCount(attention())).toBe(0);
  });

  it('counts a no-taker runner capacity row, but not a one-taker row, and adds exactly one for one of each', () => {
    // A one-taker row is information, not breakage (HA-2079's decision); only
    // a no-taker row is a person's to fix.
    expect(attentionCount(attention({ runnerCapacity: [runnerCapacity({ takers: ['Nathan-PC'] })] }))).toBe(0);
    expect(attentionCount(attention({ runnerCapacity: [runnerCapacity({ takers: [] })] }))).toBe(1);
    expect(
      attentionCount(
        attention({
          runnerCapacity: [runnerCapacity({ projectKey: 'AER', takers: ['Nathan-PC'] }), runnerCapacity({ projectKey: 'HA', takers: [] })],
        }),
      ),
    ).toBe(1);
    expect(attentionCount(attention())).toBe(0);
  });

  it('counts a backup as one row, adds it to the other sources, and counts null or absent as zero', () => {
    // Nothing in the loop will fix a folder, so it is a person's, every time.
    const backup = { sentence: 'The last backup failed.', lastSuccessfulAt: null };

    expect(attentionCount(attention({ backup }))).toBe(1);
    expect(attentionCount(attention({ reviews: [review()], backup }))).toBe(2);
    expect(attentionCount(attention({ backup: null }))).toBe(0);
    expect(attentionCount(attention())).toBe(0);
    expect(attentionTone(attention({ backup }))).toBe('asking');
  });

  it('does not count a runner out of usage, and stays resting for it', () => {
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

  it('counts a board-held stalled row, but not a loop-kind row or a person-held row', () => {
    // Nothing dispatches into a board-held row, so every one is a person's to
    // break - the same reasoning as `deadlocked`.
    expect(attentionCount(attention({ stalled: [stalledIssue({ kind: 'person', held: false })] }))).toBe(1);
    expect(attentionCount(attention({ stalled: [stalledIssue({ kind: 'loop' })] }))).toBe(0);
    expect(attentionCount(attention({ stalled: [stalledIssue({ kind: 'person', held: true })] }))).toBe(0);
  });

  it('counts only the board-held rows in a mixed list, alongside reviews and questions', () => {
    expect(
      attentionCount(
        attention({
          reviews: [review()],
          questions: [question()],
          stalled: [
            stalledIssue({ key: 'AER-26', kind: 'person', held: false }),
            stalledIssue({ key: 'AER-27', kind: 'loop' }),
            stalledIssue({ key: 'AER-28', kind: 'person', held: true }),
          ],
        }),
      ),
    ).toBe(3);
  });

  it('reads an absent stalled as none, for a board that predates it', () => {
    expect(attentionCount(attention({ stalled: undefined }))).toBe(0);
  });
});

describe('pillCount', () => {
  it('counts each of the four sections that draw a pill, alone', () => {
    expect(pillCount(attention({ deadlocked: [deadlocked()] }))).toBe(1);
    expect(pillCount(attention({ trunkBuilds: [trunkBuild(), trunkBuild()] }))).toBe(2);
    expect(pillCount(attention({ reviews: [review(), review(), review()] }))).toBe(3);
    expect(pillCount(attention({ questions: [question(), question(), question(), question()] }))).toBe(4);
  });

  it('is the sum of all four', () => {
    expect(
      pillCount(
        attention({
          deadlocked: [deadlocked()],
          trunkBuilds: [trunkBuild(), trunkBuild()],
          reviews: [review(), review(), review()],
          questions: [question(), question(), question(), question()],
        }),
      ),
    ).toBe(10);
  });

  it('does not count a section that lights the control but draws no pill', () => {
    // approvedHeld and landingHeld are in attentionCount and have panel rows,
    // but the bar has no pill for them, so the folded total must not either.
    expect(pillCount(attention({ approvedHeld: [approvedHeld()] }))).toBe(0);
    expect(attentionCount(attention({ approvedHeld: [approvedHeld()] }))).toBe(1);
  });

  it('does not count a backup, which lights the control but draws no pill', () => {
    const backup = { sentence: 'The last backup failed.', lastSuccessfulAt: null };

    expect(pillCount(attention({ backup }))).toBe(0);
    expect(attentionCount(attention({ backup }))).toBe(1);
  });

  it('does not count a waiting-on-a-runner row, which lights the control but draws no pill', () => {
    expect(pillCount(attention({ waitingOnARunner: [waitingOnARunner()] }))).toBe(0);
    expect(attentionCount(attention({ waitingOnARunner: [waitingOnARunner()] }))).toBe(1);
  });

  it('does not count a runner capacity row, which lights the control but draws no pill', () => {
    expect(pillCount(attention({ runnerCapacity: [runnerCapacity({ takers: [] })] }))).toBe(0);
    expect(attentionCount(attention({ runnerCapacity: [runnerCapacity({ takers: [] })] }))).toBe(1);
  });

  it('is zero before the first answer', () => {
    expect(pillCount(null)).toBe(0);
  });

  it('does not count a stalled row, board-held or otherwise, which lights the control but draws no pill', () => {
    expect(pillCount(attention({ stalled: [stalledIssue({ kind: 'person', held: false })] }))).toBe(0);
    expect(attentionCount(attention({ stalled: [stalledIssue({ kind: 'person', held: false })] }))).toBe(1);
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
      '1 question to answer, 2 pull requests to review',
    );
  });

  it('drops the half that is empty', () => {
    expect(attentionLabel(attention({ reviews: [review()] }))).toBe('1 pull request to review');
    expect(attentionLabel(attention({ questions: [question(), question()] }))).toBe('2 questions to answer');
  });

  it('pluralises each half on its own count', () => {
    expect(attentionLabel(attention({ reviews: [review()], questions: [question(), question()] }))).toBe(
      '2 questions to answer, 1 pull request to review',
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

  it('names a deadlocked issue, pluralised on its own count', () => {
    expect(attentionLabel(attention({ deadlocked: [deadlocked()] }))).toBe('1 issue deadlocked');
    expect(attentionLabel(attention({ deadlocked: [deadlocked(), deadlocked({ key: 'AER-23' })] }))).toBe(
      '2 issues deadlocked',
    );
  });

  it('names a stalled issue, pluralised on its own count, counting only board-held rows', () => {
    expect(attentionLabel(attention({ stalled: [stalledIssue({ kind: 'person', held: false })] }))).toBe(
      '1 issue stalled',
    );
    expect(
      attentionLabel(
        attention({
          stalled: [
            stalledIssue({ key: 'AER-26', kind: 'person', held: false }),
            stalledIssue({ key: 'AER-27', kind: 'person', held: false }),
          ],
        }),
      ),
    ).toBe('2 issues stalled');
  });

  it('says nothing is waiting when stalled is empty or absent', () => {
    expect(attentionLabel(attention({ stalled: [] }))).toBe('Nothing is waiting on you');
    expect(attentionLabel(attention({ stalled: undefined }))).toBe('Nothing is waiting on you');
  });

  it('says nothing is waiting when stalled has only a loop-kind row or a person-held row', () => {
    expect(attentionLabel(attention({ stalled: [stalledIssue({ kind: 'loop' })] }))).toBe('Nothing is waiting on you');
    expect(attentionLabel(attention({ stalled: [stalledIssue({ kind: 'person', held: true })] }))).toBe(
      'Nothing is waiting on you',
    );
  });

  it('puts a stalled issue after questions and before pull requests', () => {
    expect(
      attentionLabel(
        attention({
          questions: [question()],
          stalled: [stalledIssue({ kind: 'person', held: false })],
          reviews: [review()],
        }),
      ),
    ).toBe('1 question to answer, 1 issue stalled, 1 pull request to review');
  });

  it('says a backup needs attention, after the trunk builds and before the pull requests', () => {
    const backup = { sentence: 'The last backup failed.', lastSuccessfulAt: null };

    expect(attentionLabel(attention({ backup }))).toContain('backup needs attention');
    expect(attentionLabel(attention({ backup: null }))).toBe('Nothing is waiting on you');
    expect(attentionLabel(attention({ trunkBuilds: [trunkBuild()], backup, reviews: [review()] }))).toBe(
      '1 trunk build failing, backup needs attention, 1 pull request to review',
    );
  });

  it('puts trunk builds ahead of deadlocked, pull requests and questions', () => {
    expect(
      attentionLabel(attention({ deadlocked: [deadlocked()], trunkBuilds: [trunkBuild()], reviews: [review()] })),
    ).toBe('1 trunk build failing, 1 issue deadlocked, 1 pull request to review');
  });

  it('combines all three parts, trunk builds first', () => {
    expect(
      attentionLabel(attention({ trunkBuilds: [trunkBuild()], reviews: [review()], questions: [question()] })),
    ).toBe('1 trunk build failing, 1 question to answer, 1 pull request to review');
  });

  it('adds a runner out of usage as its own phrase, singular and plural', () => {
    expect(attentionLabel(attention({ exhaustedRunners: [exhaustedRunner()] }))).toBe(
      '1 runner is out of usage',
    );
    expect(attentionLabel(attention({ exhaustedRunners: [exhaustedRunner(), exhaustedRunner()] }))).toBe(
      '2 runners are out of usage',
    );
  });

  it('joins it onto the human halves rather than replacing them', () => {
    expect(attentionLabel(attention({ reviews: [review()], exhaustedRunners: [exhaustedRunner()] }))).toBe(
      '1 pull request to review, 1 runner is out of usage',
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

describe('approvedHeldEmptyWords', () => {
  it('says nothing approved has stopped merging', () => {
    expect(approvedHeldEmptyWords()).toBe('Nothing approved has stopped merging.');
  });
});

describe('deadlockedEmptyWords', () => {
  it('says nothing is deadlocked', () => {
    expect(deadlockedEmptyWords()).toBe('Nothing is deadlocked.');
  });
});

describe('waitingOnARunnerEmptyWords', () => {
  it('says nothing is waiting on a runner', () => {
    expect(waitingOnARunnerEmptyWords()).toBe('Nothing is waiting on a runner.');
  });
});

describe('runnerCapacityWords', () => {
  it('names the one taker, pluralising the step count', () => {
    expect(runnerCapacityWords(runnerCapacity({ steps: 3, takers: ['Nathan-PC'] }))).toBe('3 clear steps: only Nathan-PC can take them');
    expect(runnerCapacityWords(runnerCapacity({ steps: 1, takers: ['Nathan-PC'] }))).toBe('1 clear step: only Nathan-PC can take them');
  });

  it('joins one reason with no taker', () => {
    expect(
      runnerCapacityWords(runnerCapacity({ takers: [], reasons: [{ runner: 'Nathan-PC', reason: 'out of usage' }] })),
    ).toBe('no live runner can take them: Nathan-PC: out of usage');
  });

  it('joins two or more reasons with no taker, separated by "; "', () => {
    expect(
      runnerCapacityWords(
        runnerCapacity({
          takers: [],
          reasons: [
            { runner: 'Nathan-PC', reason: 'out of usage' },
            { runner: 'Nathan-MBP', reason: 'wrong model' },
          ],
        }),
      ),
    ).toBe('no live runner can take them: Nathan-PC: out of usage; Nathan-MBP: wrong model');
  });
});

describe('runnerCapacityEmptyWords', () => {
  it("says every project's next steps have two or more live runners", () => {
    expect(runnerCapacityEmptyWords()).toBe("Every project's next steps have two or more live runners.");
  });
});

describe('trainStalledWords', () => {
  it('names the runner passed over and the one the train was handed to', () => {
    expect(trainStalledWords(trainStalled())).toBe("laptop:/work/aerie was passed over on Aerie's train - handed to desk:/work/aerie");
  });

  it('says no other runner is eligible when none was elected', () => {
    expect(trainStalledWords(trainStalled({ electedRunner: null }))).toBe(
      "laptop:/work/aerie was passed over on Aerie's train - no other runner is eligible",
    );
  });

  it('is not counted: the row waits on a runner that may never beat again', () => {
    expect(attentionCount(attention({ trainStalled: [trainStalled()] }))).toBe(0);
    expect(pillCount(attention({ trainStalled: [trainStalled()] }))).toBe(0);
  });
});

describe('noCodeHeldEmptyWords', () => {
  it('says nothing finished with no code is waiting', () => {
    expect(noCodeHeldEmptyWords()).toBe('Nothing finished with no code is waiting on you.');
  });
});

describe('landingHeldEmptyWords', () => {
  it('says nothing landed has stopped short of done', () => {
    expect(landingHeldEmptyWords()).toBe('Nothing landed has stopped short of done.');
  });
});

describe('questionEmptyWords', () => {
  it('says nothing is waiting on an answer', () => {
    expect(questionEmptyWords()).toBe('Nothing is waiting on an answer.');
  });
});

describe('stalledEmptyWords', () => {
  it('says nothing is stalled', () => {
    expect(stalledEmptyWords()).toBe('Nothing is stalled.');
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

describe('stalledWords', () => {
  const STALL_RESUME_SECONDS = 900;

  it('prefixes "resumes" for a loop-kind row within its window', () => {
    expect(stalledWords(stalledIssue({ kind: 'loop', stalledAt: ago(12 * 60_000) }), STALL_RESUME_SECONDS, NOW)).toBe(
      'resumes in 3 minutes',
    );
  });

  it('reads bare "any moment now" for a loop-kind row past its window', () => {
    // The one exception: that string already reads as a complete sentence on
    // its own, where "in 12 minutes" does not without the "resumes" prefix.
    expect(stalledWords(stalledIssue({ kind: 'loop', stalledAt: ago(16 * 60_000) }), STALL_RESUME_SECONDS, NOW)).toBe(
      'any moment now',
    );
  });

  it('says held by a person for a held row', () => {
    expect(stalledWords(stalledIssue({ kind: 'person', held: true }), STALL_RESUME_SECONDS, NOW)).toBe(
      'held by a person',
    );
  });

  it('says the queue\'s own sentence for a board-held row', () => {
    expect(
      stalledWords(
        stalledIssue({ kind: 'person', held: false, sentence: 'Let go 3 times in a row.' }),
        STALL_RESUME_SECONDS,
        NOW,
      ),
    ).toBe('Let go 3 times in a row.');
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

describe('closesOnPress', () => {
  const plain = { button: 0, metaKey: false, ctrlKey: false, shiftKey: false, altKey: false };

  it('closes on a same-tab link', () => {
    expect(closesOnPress({ href: '/issues/HA-12', target: null }, plain)).toBe(true);
    expect(closesOnPress({ href: '/issues/HA-12', target: '_self' }, plain)).toBe(true);
  });

  it('stays open for a link that opens a new tab', () => {
    expect(closesOnPress({ href: 'https://example.test/pull/1', target: '_blank' }, plain)).toBe(false);
  });

  it('stays open for a button, which has no link', () => {
    expect(closesOnPress(null, plain)).toBe(false);
  });

  it('stays open for an anchor with nowhere to go', () => {
    expect(closesOnPress({ href: null, target: null }, plain)).toBe(false);
    expect(closesOnPress({ href: '', target: null }, plain)).toBe(false);
  });

  it('stays open for a press that opens elsewhere', () => {
    const link = { href: '/issues/HA-12', target: null };
    expect(closesOnPress(link, { ...plain, metaKey: true })).toBe(false);
    expect(closesOnPress(link, { ...plain, ctrlKey: true })).toBe(false);
    expect(closesOnPress(link, { ...plain, shiftKey: true })).toBe(false);
    expect(closesOnPress(link, { ...plain, altKey: true })).toBe(false);
    expect(closesOnPress(link, { ...plain, button: 1 })).toBe(false);
  });
});

const backupAttention: BackupAttention = { sentence: 'Backup failed last night.', lastSuccessfulAt: null };

describe('reviewWordsCarryAFact', () => {
  it('is false when neither emptiness has a count', () => {
    expect(reviewWordsCarryAFact(attention())).toBe(false);
  });

  it('is true when issues are in review with no pull request recorded', () => {
    expect(reviewWordsCarryAFact(attention({ inReviewWithoutPullRequest: 3 }))).toBe(true);
  });

  it('is true when pull requests are held back from review', () => {
    expect(reviewWordsCarryAFact(attention({ reviewsHeldBack: 2 }))).toBe(true);
  });

  it('is null-safe', () => {
    expect(reviewWordsCarryAFact(null)).toBe(false);
  });

  it('agrees with reviewEmptyWords: true exactly when it says more than "Nothing is up for review."', () => {
    for (const a of [attention(), attention({ inReviewWithoutPullRequest: 3 }), attention({ reviewsHeldBack: 2 }), null]) {
      expect(reviewWordsCarryAFact(a)).toBe(reviewEmptyWords(a) !== 'Nothing is up for review.');
    }
  });
});

describe('sectionCollapsed', () => {
  it('is false when there are rows', () => {
    expect(sectionCollapsed(3)).toBe(false);
  });

  it('is true when there are no rows and no fact', () => {
    expect(sectionCollapsed(0)).toBe(true);
    expect(sectionCollapsed(0, false)).toBe(true);
  });

  it('is false when there are no rows but the empty state carries a fact', () => {
    expect(sectionCollapsed(0, true)).toBe(false);
  });
});

describe('groupCollapsed', () => {
  it('is true when every section is empty', () => {
    expect(groupCollapsed([0, 0, 0])).toBe(true);
  });

  it('is true for an empty list of sections', () => {
    expect(groupCollapsed([])).toBe(true);
  });

  it('is false when one section has a row', () => {
    expect(groupCollapsed([0, 1, 0])).toBe(false);
  });
});

describe('humanSectionRowCounts', () => {
  it('is all zeros, one per section, when there is no attention at all', () => {
    const counts = humanSectionRowCounts(null);
    expect(counts).toHaveLength(12);
    expect(counts).toEqual(new Array(12).fill(0));
  });

  it('pins the order: trunk builds, backup, stalled trains, deadlocked, questions, stalled, reviews, approved-held, landing-held, no-code-held, runner capacity, waiting on a runner', () => {
    expect(
      humanSectionRowCounts(
        attention({
          trunkBuilds: Array(2).fill(trunkBuild()),
          backup: backupAttention,
          trainStalled: Array(3).fill(trainStalled()),
          deadlocked: Array(4).fill(deadlocked()),
          questions: Array(5).fill(question()),
          stalled: Array(6).fill(stalledIssue()),
          reviews: Array(7).fill(review()),
          approvedHeld: Array(8).fill(approvedHeld()),
          landingHeld: Array(9).fill(landingHeld()),
          noCodeHeld: Array(10).fill(noCodeHeld()),
          runnerCapacity: Array(11).fill(runnerCapacity()),
          waitingOnARunner: Array(12).fill(waitingOnARunner()),
        }),
      ),
    ).toEqual([2, 1, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
  });

  it('counts backup as one row when present, none when null', () => {
    expect(humanSectionRowCounts(attention({ backup: null }))[1]).toBe(0);
    expect(humanSectionRowCounts(attention({ backup: backupAttention }))[1]).toBe(1);
  });

  it('raises the review row to 1 when reviewWordsCarryAFact is true, even with no reviews', () => {
    expect(humanSectionRowCounts(attention({ reviews: [] }))[6]).toBe(0);
    expect(humanSectionRowCounts(attention({ reviews: [], inReviewWithoutPullRequest: 1 }))[6]).toBe(1);
    expect(humanSectionRowCounts(attention({ reviews: [], reviewsHeldBack: 1 }))[6]).toBe(1);
  });
});

describe('agentSectionRowCounts', () => {
  it('counts conflicts then failing builds', () => {
    expect(agentSectionRowCounts(attention({ conflicts: [conflict(), conflict()], failingBuilds: [] }))).toEqual([2, 0]);
    expect(
      agentSectionRowCounts(
        attention({ conflicts: [], failingBuilds: [{ key: 'AER-1', title: 'A build that failed', type: 'story', pullRequestUrl: null, checks: [] }] }),
      ),
    ).toEqual([0, 1]);
  });

  it('is null-safe', () => {
    expect(agentSectionRowCounts(null)).toEqual([0, 0]);
  });
});
