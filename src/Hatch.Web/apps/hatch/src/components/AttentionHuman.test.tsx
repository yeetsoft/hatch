import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { AttentionHuman } from './AttentionHuman';
import { findingWords, gooseStoppedWords, approvedHeldEmptyWords, ballotRowWords, contributionsHeldEmptyWords, deadlockedEmptyWords, heldAtProjectLineEmptyWords, landingHeldEmptyWords, noCodeHeldEmptyWords, questionEmptyWords, reviewEmptyWords, runnerCapacityEmptyWords, runnerCapacityWords, stalledEmptyWords, trainStalledReasonWords, trainStalledWords, trunkBuildEmptyWords, waitingOnARunnerEmptyWords } from '../lib/attention';
import type { Attention, AttentionFinding, AttentionGoose, ApprovedHeld, ContributionHeld, Deadlocked, LandingHeld, NoCodeHeld, ProjectLineHeld, Question, Review, RunnerCapacity, StalledIssue, TrainStalled, TrunkBuild, WaitingOnARunner } from '../types';

/* No DOM here, so this is structural - react-dom/server, the way
   Confirmations.test.tsx reads. What it pins is AC1's "same order, same
   empty wordings" against a refactor that might reorder the extraction by
   accident. */
const noop = () => Promise.resolve();

// react-dom/server escapes an apostrophe as `&#x27;` - runnerCapacityEmptyWords
// is the only empty wording here that carries one.
const htmlEscaped = (s: string): string => s.replace(/'/g, '&#x27;');

let dialogProps: {
  open: boolean;
  buildId: number;
  onClose: () => void;
  onFiled: (created: unknown) => void;
} | null = null;
vi.mock('./TrunkBuildBugDialog', () => ({
  TrunkBuildBugDialog: (props: typeof dialogProps) => {
    dialogProps = props;
    return null;
  },
}));

let findingDialogProps: { open: boolean; findingId: number; projectKey: string | null } | null = null;
vi.mock('./FindingBugDialog', () => ({
  FindingBugDialog: (props: typeof findingDialogProps) => {
    findingDialogProps = props;
    return null;
  },
}));

// Same six-field shape lib/attention.test.ts's own trunkBuild() builds - a
// test fixture, not imported.
const trunkBuild = (over: Partial<TrunkBuild> = {}): TrunkBuild => ({
  id: 7,
  remote: 'https://forge.example/owner/repo.git',
  canonical: 'forge.example/owner/repo',
  trunk: 'main',
  sha: '1111111111111111111111111111111111111111',
  shaSince: new Date().toISOString(),
  verdict: 'failed',
  failing: [{ name: 'CI', url: null }],
  checkedAt: new Date().toISOString(),
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  bugIssueKey: null,
  projectKey: null,
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

const finding = (over: Partial<AttentionFinding> = {}): AttentionFinding => ({
  id: 1,
  check: 'train-ready',
  projectKey: 'AER',
  sentence: 'AER has a train ready and nobody is driving it.',
  beganAt: new Date(Date.now() - 3 * 3_600_000).toISOString(),
  openedAt: new Date(Date.now() - 2 * 3_600_000).toISOString(),
  bugIssueKey: null,
  issueKey: null,
  ...over,
});

const goose = (over: Partial<AttentionGoose> = {}): AttentionGoose => ({
  lastSweepAt: new Date(Date.now() - 12 * 60_000).toISOString(),
  intervalSeconds: 60,
  stopped: false,
  ...over,
});

const question = (over: Partial<Question> = {}): Question => ({
  id: 1,
  issueKey: 'AER-30',
  issueTitle: 'A story with a question',
  body: 'Which of the two should it be?',
  askedBy: 'a-runner',
  askedAt: new Date().toISOString(),
  options: null,
  answers: [],
  level: 'hold',
  lookBack: false,
  ...over,
});

const review = (over: Partial<Review> = {}): Review => ({
  key: 'AER-16',
  title: 'A story up for review',
  type: 'story',
  pullRequestUrl: 'https://forge.example/pulls/16',
  buildState: 'unknown',
  holdsTrunk: null,
  trunk: null,
  playbookFilePath: null,
  dependencyFilePath: null,
  ...over,
});

const approvedHeld = (over: Partial<ApprovedHeld> = {}): ApprovedHeld => ({
  key: 'AER-14',
  title: 'A story that stopped merging',
  type: 'story',
  pullRequestUrl: 'https://forge.example/pulls/14',
  sentence: 'Its build is failing.',
  ...over,
});

const landingHeld = (over: Partial<LandingHeld> = {}): LandingHeld => ({
  key: 'AER-15',
  title: 'A story that landed and is not done',
  type: 'story',
  sentence: 'Its pull request merged but the issue is not done.',
  ...over,
});

const noCodeHeld = (over: Partial<NoCodeHeld> = {}): NoCodeHeld => ({
  key: 'AER-18',
  title: 'A task that needed no code',
  type: 'task',
  reason: 'The setting was already off.',
  ...over,
});

const contributionHeld = (over: Partial<ContributionHeld> = {}): ContributionHeld => ({
  key: 'AER-31',
  title: 'A contribution with a red build',
  type: 'task',
  pullRequestUrl: 'https://forge.example/pulls/31',
  sentence: 'Its build failed.',
  originInstance: null,
  originKey: null,
  ...over,
});

const deadlocked = (over: Partial<Deadlocked> = {}): Deadlocked => ({
  key: 'AER-16',
  title: 'A story stuck in a ring',
  type: 'story',
  sentence: 'deadlocked - it waits on AER-17, which waits on it.',
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

const heldAtProjectLine = (over: Partial<ProjectLineHeld> = {}): ProjectLineHeld => ({
  key: 'AER-40',
  title: 'A story under another project’s epic',
  type: 'story',
  projectKey: 'AER',
  sentence: 'pre-approved on an ancestor in another project, not honoured in AER - its setter has no say there, so a person moves it',
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

const trainStalled = (over: Partial<TrainStalled> = {}): TrainStalled => ({
  projectKey: 'AER',
  projectName: 'Aerie',
  passedOverRunner: 'laptop:/work/aerie',
  electedRunner: 'desk:/work/aerie',
  headKey: 'AER-31',
  at: '2026-09-09T12:00:00Z',
  reason: null,
  ...over,
});

const stalledIssue = (over: Partial<StalledIssue> = {}): StalledIssue => ({
  key: 'AER-26',
  title: 'A story nobody has touched',
  type: 'story',
  stalledAt: new Date(Date.now() - 60_000).toISOString(),
  stalledWhy: 'Left where it was found.',
  held: false,
  letGo: 1,
  sentence: 'The loop let it go once.',
  kind: 'loop',
  ...over,
});

beforeEach(() => {
  vi.stubGlobal('window', { location: { pathname: '/' } });
  dialogProps = null;
  findingDialogProps = null;
});
afterEach(() => vi.unstubAllGlobals());

describe('AttentionHuman', () => {
  it('draws trunk builds first, then deadlocked, questions, stalled issues, pull requests, approved, landed, finished with no code, contributions runners will not work, runner capacity, then waiting on a runner, then waiting on another project’s approver, each with the panel’s own empty wording', () => {
    const html = renderToStaticMarkup(<AttentionHuman attention={null} now={new Date()} reload={noop} startOpen />);

    const headings = [...html.matchAll(/<h3 class="hatch-attention-heading">(?:<button[^>]*>)?([^<]*)(?:<\/button>)?<\/h3>/g)].map((m) => m[1]);
    expect(headings).toEqual([
      'Trunk builds that fail',
      'Deadlocked',
      'Questions to answer',
      'Stalled issues',
      'Pull requests to review',
      'Approved, not merging',
      'Landed, not done',
      'Finished with no code',
      'Contributions runners will not work',
      'Runner capacity',
      'Waiting on a runner',
      'Waiting on another project&#x27;s approver',
    ]);

    const trunkAt = html.indexOf(trunkBuildEmptyWords());
    const deadlockedAt = html.indexOf(deadlockedEmptyWords());
    const questionAt = html.indexOf(questionEmptyWords());
    const stalledAt = html.indexOf(stalledEmptyWords());
    const reviewAt = html.indexOf(reviewEmptyWords(null));
    const approvedHeldAt = html.indexOf(approvedHeldEmptyWords());
    const landingHeldAt = html.indexOf(landingHeldEmptyWords());
    const noCodeHeldAt = html.indexOf(noCodeHeldEmptyWords());
    const contributionsHeldAt = html.indexOf(contributionsHeldEmptyWords());
    const runnerCapacityAt = html.indexOf(htmlEscaped(runnerCapacityEmptyWords()));
    const waitingAt = html.indexOf(waitingOnARunnerEmptyWords());
    const projectLineAt = html.indexOf(htmlEscaped(heldAtProjectLineEmptyWords()));

    expect(trunkAt).toBeGreaterThan(-1);
    expect(trunkAt).toBeLessThan(deadlockedAt);
    expect(deadlockedAt).toBeGreaterThan(-1);
    expect(deadlockedAt).toBeLessThan(questionAt);
    expect(questionAt).toBeLessThan(stalledAt);
    expect(stalledAt).toBeGreaterThan(-1);
    expect(stalledAt).toBeLessThan(reviewAt);
    expect(reviewAt).toBeLessThan(approvedHeldAt);
    expect(approvedHeldAt).toBeLessThan(landingHeldAt);
    expect(landingHeldAt).toBeLessThan(noCodeHeldAt);
    expect(noCodeHeldAt).toBeLessThan(contributionsHeldAt);
    expect(contributionsHeldAt).toBeLessThan(runnerCapacityAt);
    expect(runnerCapacityAt).toBeGreaterThan(-1);
    expect(runnerCapacityAt).toBeLessThan(waitingAt);
    expect(waitingAt).toBeGreaterThan(-1);
    expect(waitingAt).toBeLessThan(projectLineAt);
    expect(projectLineAt).toBeGreaterThan(-1);
  });

  it('draws a stalled row of each shape with its own lines, linking to the issue', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman
          attention={attention({
            stalled: [
              stalledIssue({ key: 'AER-26', title: 'Loop row' }),
              stalledIssue({ key: 'AER-27', title: 'Person row', kind: 'person', held: true, stalledAt: null, stalledWhy: null, sentence: null }),
              stalledIssue({
                key: 'AER-28',
                title: 'Board row',
                kind: 'person',
                held: false,
                stalledWhy: 'Let go three times.',
                sentence: 'Held for a person: it has been let go three times.',
              }),
            ],
            stallResumeSeconds: 600,
          })}
          now={new Date()}
          reload={noop}
        />
      </MemoryRouter>,
    );

    expect(html).toContain('Left where it was found.');
    expect(html).toContain('resumes in 9 minutes');
    expect(html).toContain('held by a person');
    expect(html).toContain('Held for a person: it has been let go three times.');
    expect(html).not.toContain(stalledEmptyWords());

    const rows = html.split('<li').slice(1);
    const rowFor = (key: string) => rows.find((r) => r.includes(`href="/issues/${key}"`))!;
    const files = (r: string) => r.split('hatch-attention-files').length - 1;
    expect(rowFor('AER-26')).toContain('Loop row');
    expect(rowFor('AER-27')).toContain('Person row');
    expect(rowFor('AER-28')).toContain('Board row');
    expect(files(rowFor('AER-26'))).toBe(2);
    expect(files(rowFor('AER-27'))).toBe(1);
    expect(files(rowFor('AER-28'))).toBe(2);
  });

  it('reads a loop row past its window as any moment now, not resumes any moment now', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman
          attention={attention({
            stalled: [stalledIssue({ stalledAt: new Date(Date.now() - 20 * 60_000).toISOString() })],
            stallResumeSeconds: 600,
          })}
          now={new Date()}
          reload={noop}
        />
      </MemoryRouter>,
    );

    expect(html).toContain('any moment now');
    expect(html).not.toContain('resumes any moment now');
  });

  it('draws the empty wording and no list when the server sends no stalled rows', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ stalled: undefined })} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

    const section = html.slice(html.indexOf('>Stalled issues</'), html.indexOf('>Pull requests to review</'));
    expect(section).toContain('Nothing is stalled.');
    expect(section).not.toContain('<ul');
  });

  it('draws Backups above Deadlocked, with the sentence, the last success and a link to Settings', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman
          attention={attention({ backup: { sentence: 'The last backup failed.', lastSuccessfulAt: '2026-09-09T12:00:00Z' } })}
          now={new Date()}
          reload={noop}
        />
      </MemoryRouter>,
    );

    const headings = [...html.matchAll(/<h3 class="hatch-attention-heading">(?:<button[^>]*>)?([^<]*)(?:<\/button>)?<\/h3>/g)].map((m) => m[1]);
    expect(headings[0]).toBe('Trunk builds that fail');
    expect(headings[1]).toBe('Backups');
    expect(headings[2]).toBe('Deadlocked');
    expect(html.indexOf('>Backups</h3>')).toBeLessThan(html.indexOf('>Deadlocked</'));
    expect(html).toContain('The last backup failed.');
    expect(html).toContain('Last successful backup:');
    expect(html).toContain('href="/settings"');
  });

  it('says a backup that never succeeded has never succeeded', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman
          attention={attention({ backup: { sentence: 'The last backup failed.', lastSuccessfulAt: null } })}
          now={new Date()}
          reload={noop}
        />
      </MemoryRouter>,
    );

    expect(html).toContain('There has never been a successful backup.');
  });

  it('draws no Backups section for a null backup or an absent one', () => {
    for (const over of [{ backup: null }, {}]) {
      const html = renderToStaticMarkup(
        <MemoryRouter>
          <AttentionHuman attention={attention(over)} now={new Date()} reload={noop} />
        </MemoryRouter>,
      );

      expect(html).not.toContain('Backups');
      expect(html).not.toContain('href="/settings"');
    }
  });

  it('shows the empty words when nothing is waiting on a runner, with no link to the Runners page', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ waitingOnARunner: [] })} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

    expect(html).toContain(waitingOnARunnerEmptyWords());
    expect(html).not.toContain('href="/runners"');
  });

  it('lists a waiting-on-a-runner row’s key, title and what is missing, linked to the issue and once to Runners', () => {
    const row = waitingOnARunner();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ waitingOnARunner: [row, waitingOnARunner({ key: 'AER-25' })] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(waitingOnARunnerEmptyWords());
    expect(html).toContain('>Waiting on a runner</');
    expect(html).toContain(row.key);
    expect(html).toContain(row.title);
    expect(html).toContain(`in ${row.move} - ${row.missing}`);
    expect(html).toContain(`href="/issues/${row.key}"`);
    expect(html.match(/href="\/runners"/g)).toHaveLength(1);
  });

  it('shows the empty words when runnerCapacity is empty or absent, with no rows', () => {
    for (const over of [{ runnerCapacity: [] }, {}]) {
      const html = renderToStaticMarkup(
        <MemoryRouter>
          <AttentionHuman attention={attention(over)} now={new Date()} reload={noop} startOpen />
        </MemoryRouter>,
      );

      expect(html).toContain(htmlEscaped(runnerCapacityEmptyWords()));
    }
  });

  it('lists a runner capacity row’s project key, words and every key linked to the issue', () => {
    const row = runnerCapacity();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ runnerCapacity: [row] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(runnerCapacityEmptyWords());
    expect(html).toContain('>Runner capacity</');
    expect(html).toContain(row.projectKey);
    expect(html).toContain(runnerCapacityWords(row));
    for (const key of row.keys) {
      expect(html).toContain(`href="/issues/${key}"`);
    }
    // No rows under Waiting on a runner here, so no link to it either - this
    // section draws no Runners-page link of its own.
    expect(html.match(/href="\/runners"/g) ?? []).toHaveLength(0);
  });

  it('shows the empty words when nothing is deadlocked', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ deadlocked: [] })} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

    expect(html).toContain(deadlockedEmptyWords());
  });

  it('draws no Stalled merge trains section when the list is absent or empty', () => {
    for (const a of [null, attention(), attention({ trainStalled: [] })]) {
      const html = renderToStaticMarkup(
        <MemoryRouter>
          <AttentionHuman attention={a} now={new Date()} reload={noop} />
        </MemoryRouter>,
      );

      expect(html).not.toContain('Stalled merge trains');
    }
  });

  it('lists a stalled train’s head key, project and words under Stalled merge trains, linked to the head issue', () => {
    const row = trainStalled();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ trainStalled: [row] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).toContain('>Stalled merge trains</h3>');
    expect(html).toContain(row.headKey);
    expect(html).toContain(row.projectName);
    expect(html).toContain(trainStalledWords(row).replace("'", '&#x27;'));
    expect(html).toContain(`href="/issues/${row.headKey}"`);
    expect(html.indexOf('>Stalled merge trains</h3>')).toBeLessThan(html.indexOf('>Deadlocked</'));
  });

  it('draws a stalled train’s reason as a second line, and none when the head has no comment', () => {
    const render = (row: TrainStalled) =>
      renderToStaticMarkup(
        <MemoryRouter>
          <AttentionHuman attention={attention({ trainStalled: [row] })} now={new Date()} reload={noop} />
        </MemoryRouter>,
      );

    const withReason = trainStalled({ reason: 'The merge conflicts with main.' });
    expect(render(withReason)).toContain(trainStalledReasonWords(withReason)!);
    expect(render(trainStalled()).match(/hatch-attention-files/g)?.length).toBe(1);
  });

  it('lists a deadlocked row’s key, title and sentence under Deadlocked, linked to the issue', () => {
    const row = deadlocked();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ deadlocked: [row] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(deadlockedEmptyWords());
    expect(html).toContain('>Deadlocked</');
    expect(html).toContain(row.key);
    expect(html).toContain(row.title);
    expect(html).toContain(row.sentence);
    expect(html).toContain(`href="/issues/${row.key}"`);
  });

  it('shows the empty words when nothing landed has stopped short of done', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ landingHeld: [] })} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

    expect(html).toContain(landingHeldEmptyWords());
  });

  it('lists a landed row’s key, title and sentence under Landed, not done, linked to the issue', () => {
    const held = landingHeld();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ landingHeld: [held] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(landingHeldEmptyWords());
    expect(html).toContain(held.key);
    expect(html).toContain(held.title);
    expect(html).toContain(held.sentence);
    expect(html).toContain(`href="/issues/${held.key}"`);
  });

  it('shows the empty words when nothing finished with no code is waiting', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ noCodeHeld: [] })} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

    expect(html).toContain(noCodeHeldEmptyWords());
  });

  it('lists a no-code row’s key, title and reason under Finished with no code, linked to the issue', () => {
    const held = noCodeHeld();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ noCodeHeld: [held] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(noCodeHeldEmptyWords());
    expect(html).toContain(held.key);
    expect(html).toContain(held.title);
    expect(html).toContain(held.reason);
    expect(html).toContain(`href="/issues/${held.key}"`);
  });

  it('shows the empty words when no contribution in review is waiting', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ contributionsHeld: [] })} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

    expect(html).toContain(contributionsHeldEmptyWords());
  });

  it('lists a contribution row’s key, title, pull request, sentence and origin, the key linked to the issue and the pull request linked out', () => {
    const held = contributionHeld({ originInstance: 'upstream', originKey: 'UP-9' });

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ contributionsHeld: [held] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).toContain('Contributions runners will not work');
    expect(html).not.toContain(contributionsHeldEmptyWords());
    expect(html).toContain(held.key);
    expect(html).toContain(held.title);
    expect(html).toContain(held.sentence);
    expect(html).toContain(`href="/issues/${held.key}"`);
    expect(html).toContain(`href="${held.pullRequestUrl}"`);
    expect(html).toContain('target="_blank"');
    expect(html).toContain('rel="noreferrer noopener"');
    expect(html).toContain('mirrored from upstream&#x27;s UP-9');
  });

  it('says nothing of an origin for a contribution filed here', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ contributionsHeld: [contributionHeld()] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain('mirrored from');
  });

  it('shows the empty words when nothing approved has stopped merging', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ approvedHeld: [] })} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

    expect(html).toContain(approvedHeldEmptyWords());
  });

  it('lists a held row’s key, title and sentence under Approved, not merging', () => {
    const held = approvedHeld();

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ approvedHeld: [held] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(approvedHeldEmptyWords());
    expect(html).toContain(held.key);
    expect(html).toContain(held.title);
    expect(html).toContain(held.sentence);
  });

  it('says the playbook-file sentence for a review row that carries one', () => {
    const r = review({ playbookFilePath: '.hatch/playbooks.yaml' });

    const html = renderToStaticMarkup(
      <AttentionHuman attention={attention({ reviews: [r] })} now={new Date()} reload={noop} />,
    );

    expect(html).toContain('this change also rewrites the instructions agents follow - .hatch/playbooks.yaml');
  });

  it('draws no playbook-file sentence for a review row without one', () => {
    const r = review({ playbookFilePath: null });

    const html = renderToStaticMarkup(
      <AttentionHuman attention={attention({ reviews: [r] })} now={new Date()} reload={noop} />,
    );

    expect(html).not.toContain('this change also rewrites the instructions agents follow');
  });

  it('draws no outer heading and no .hatch-attention-group wrapper of its own', () => {
    const html = renderToStaticMarkup(<AttentionHuman attention={null} now={new Date()} reload={noop} />);

    expect(html).not.toContain('hatch-attention-group');
    expect(html).not.toContain('<h2');
  });

  it('wires a failing build’s row to its own dialog, closed until the press, filing nothing itself', () => {
    const build = trunkBuild();
    const reload = vi.fn(() => Promise.resolve());

    const html = renderToStaticMarkup(
      <AttentionHuman attention={attention({ trunkBuilds: [build] })} now={new Date()} reload={reload} />,
    );

    expect(html).toContain('File a bug');
    expect(html).not.toContain('hatch-attention-row-error');

    expect(dialogProps).toMatchObject({ open: false, buildId: build.id });
    expect(typeof dialogProps?.onClose).toBe('function');
    expect(typeof dialogProps?.onFiled).toBe('function');

    // TrunkBuildRow owns none of the filing or confirming itself - the only
    // thing its onFiled does is what the row was already doing on every other
    // reload, forwarded straight to the dialog.
    dialogProps?.onFiled({});
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('skips the button for a build that already has its bug, without a second dialog press', () => {
    const build = trunkBuild({ bugIssueKey: 'HA-1' });

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ trunkBuilds: [build] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain('File a bug');
    expect(html).toContain('HA-1');
    expect(dialogProps).toMatchObject({ open: false, buildId: build.id });
  });
});

describe('AttentionHuman questions', () => {
  const render = (questions: Question[]) =>
    renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ questions })} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

  it('opens the section with a row to the Ballot and the count, before the first question', () => {
    const html = render([
      question({ id: 1, issueKey: 'AER-30', body: 'Which of the two should it be?' }),
      question({ id: 2, issueKey: 'AER-31', body: 'Keep the old name?' }),
    ]);
    const words = ballotRowWords(2);

    expect(html).toContain('href="/ballot"');
    expect(html).toContain(words.call);
    expect(html).toContain('2 questions');
    expect(html).not.toContain(questionEmptyWords());
    expect(html.indexOf('href="/ballot"')).toBeLessThan(html.indexOf('href="/issues/AER-30"'));
  });

  it('still links each question to its issue, with its body', () => {
    const html = render([
      question({ id: 1, issueKey: 'AER-30', body: 'Which of the two should it be?' }),
      question({ id: 2, issueKey: 'AER-31', body: 'Keep the old name?' }),
    ]);

    expect(html).toContain('href="/issues/AER-30"');
    expect(html).toContain('Which of the two should it be?');
    expect(html).toContain('href="/issues/AER-31"');
    expect(html).toContain('Keep the old name?');
    expect(html.indexOf('AER-30')).toBeLessThan(html.indexOf('AER-31'));
  });

  it('counts one question in the singular', () => {
    expect(render([question()])).toContain('1 question<');
  });

  it('draws no Ballot row with no question, and keeps the empty wording', () => {
    const html = render([]);

    expect(html).not.toContain('/ballot');
    expect(html).not.toContain('Answer on the Ballot');
    expect(html).toContain(questionEmptyWords());
  });
});

describe('AttentionHuman held at a project line', () => {
  it('shows the empty words, with no link, when nothing is held at a project line', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ heldAtProjectLine: [] })} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

    expect(html).toContain('>Waiting on another project&#x27;s approver</');
    expect(html).toContain(htmlEscaped(heldAtProjectLineEmptyWords()));
  });

  it('lists each row’s key, title and sentence, linked to the issue', () => {
    const row = heldAtProjectLine();
    const other = heldAtProjectLine({ key: 'AER-41', sentence: 'its parent in another project is in progress, but the person who started it has no say in AER, so a person moves it' });

    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ heldAtProjectLine: [row, other] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).not.toContain(htmlEscaped(heldAtProjectLineEmptyWords()));
    expect(html).toContain(row.key);
    expect(html).toContain(htmlEscaped(row.title));
    expect(html).toContain(row.sentence);
    expect(html).toContain(`title="${row.sentence}"`);
    expect(html).toContain(other.sentence);
    expect(html).toContain(`href="/issues/${row.key}"`);
    expect(html).toContain(`href="/issues/${other.key}"`);
  });
});

describe('AttentionHuman folding', () => {
  // Trunk builds, Deadlocked, Questions, Stalled issues, Pull requests,
  // Approved, Landed, Finished with no code, Contributions runners will not
  // work, Runner capacity, Waiting on a runner, Waiting on another project's approver - the twelve sections AttentionSection wraps. Backups and Stalled
  // merge trains are not among them: both stay their own plain <section>.
  const emptyWords = (a: Attention | null) => [
    trunkBuildEmptyWords(),
    deadlockedEmptyWords(),
    questionEmptyWords(),
    stalledEmptyWords(),
    reviewEmptyWords(a),
    approvedHeldEmptyWords(),
    landingHeldEmptyWords(),
    noCodeHeldEmptyWords(),
    contributionsHeldEmptyWords(),
    htmlEscaped(runnerCapacityEmptyWords()),
    waitingOnARunnerEmptyWords(),
    htmlEscaped(heldAtProjectLineEmptyWords()),
  ];

  it('starts every section folded, showing no empty words, when attention is empty or null', () => {
    for (const a of [attention(), null]) {
      const html = renderToStaticMarkup(
        <MemoryRouter>
          <AttentionHuman attention={a} now={new Date()} reload={noop} />
        </MemoryRouter>,
      );

      expect(html.match(/aria-expanded="false"/g)).toHaveLength(12);
      for (const words of emptyWords(a)) {
        expect(html).not.toContain(words);
      }
    }
  });

  it('draws a section with a row plain, with no button between its heading and the next', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ deadlocked: [deadlocked()] })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    const start = html.indexOf('<h3 class="hatch-attention-heading">Deadlocked</h3>');
    expect(start).toBeGreaterThan(-1);

    const section = html.slice(start, html.indexOf('<h3', start + 1));
    expect(section).not.toContain('<button');
  });

  it('opens Pull requests to review alone when its empty words carry a fact, leaving the other eleven folded', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention({ inReviewWithoutPullRequest: 3 })} now={new Date()} reload={noop} />
      </MemoryRouter>,
    );

    expect(html).toContain('3 issues are in review with no pull request recorded.');
    expect(html).toContain('<h3 class="hatch-attention-heading">Pull requests to review</h3>');
    expect(html.match(/aria-expanded="false"/g)).toHaveLength(11);
  });

  it('starts every section open, showing every empty words string, with startOpen', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={attention()} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

    expect(html.match(/aria-expanded="true"/g)).toHaveLength(12);
    for (const words of emptyWords(attention())) {
      expect(html).toContain(words);
    }
  });
});

describe('AttentionHuman findings', () => {
  const draw = (a: Attention | null) =>
    renderToStaticMarkup(
      <MemoryRouter>
        <AttentionHuman attention={a} now={new Date()} reload={noop} startOpen />
      </MemoryRouter>,
    );

  it('draws a finding\u2019s sentence and age, linked to its issue, above Trunk builds that fail', () => {
    const row = finding({ issueKey: 'AER-9', sentence: 'AER-9 has been at the head of the train for a while.' });
    const html = draw(attention({ findings: [row] }));

    expect(html).toContain('>Findings</h3>');
    expect(html).toContain('href="/issues/AER-9"');
    expect(html).toContain(row.sentence);
    expect(html).toContain(findingWords(row, new Date()));
    expect(html.indexOf('>Findings</h3>')).toBeLessThan(html.indexOf('>Trunk builds that fail</'));
  });

  it('links a finding about no issue to its project\u2019s board', () => {
    const html = draw(attention({ findings: [finding()] }));

    expect(html).toContain('href="/board?scope=AER"');
    expect(html).not.toContain('href="/issues/');
  });

  it('shows a finding\u2019s bug as a link of its own, beside the row link and not inside it', () => {
    const html = draw(attention({ findings: [finding({ issueKey: 'AER-9', bugIssueKey: 'AER-77' })] }));

    expect(html).toContain('href="/issues/AER-77" data-discover="true">AER-77</a>');
    expect(html.match(/<a /g)?.length).toBe(3);
    expect(html).not.toMatch(/<a [^>]*>(?:(?!<\/a>).)*<a /s);
  });

  it('draws File a bug on a finding with no bug yet, beside the row link and not inside it', () => {
    const html = draw(attention({ findings: [finding({ issueKey: 'AER-9' })] }));

    expect(html).toMatch(/<button[^>]*>File a bug<\/button>/);
    expect(html.match(/<a /g)?.length).toBe(2);
    expect(html).not.toMatch(/<a [^>]*>(?:(?!<\/a>).)*<button/s);
    expect(findingDialogProps).toMatchObject({ open: false, findingId: finding().id, projectKey: finding().projectKey });
  });

  it('draws a finding’s bug key and no File a bug button once it has a bug', () => {
    const html = draw(attention({ findings: [finding({ issueKey: 'AER-9', bugIssueKey: 'AER-77' })] }));

    expect(html).toContain('>AER-77</a>');
    expect(html).not.toContain('File a bug');
  });

  it('links every finding to the goose’s page, beside the row link and not inside it', () => {
    const html = draw(attention({ findings: [finding({ issueKey: 'AER-9' })] }));

    expect(html).toContain('href="/goose" data-discover="true">Goose</a>');
    expect(html).not.toMatch(/<a [^>]*>(?:(?!<\/a>).)*<a /s);
  });

  it('says first that the goose has stopped and when it last ran, linked to its page', () => {
    const g = goose({ stopped: true });
    const html = draw(attention({ findings: [finding()], goose: g }));

    expect(html).toContain('href="/goose"');
    expect(html).not.toContain('href="/docs/the-goose"');
    expect(html).toContain(gooseStoppedWords(g, new Date()));
    expect(html.indexOf(gooseStoppedWords(g, new Date()))).toBeLessThan(html.indexOf(finding().sentence));
  });

  it('draws the stopped row alone when nothing was found', () => {
    const html = draw(attention({ goose: goose({ stopped: true, lastSweepAt: null }) }));

    expect(html).toContain('It has not run since the server started.');
  });

  it('draws nothing when nothing was found and the goose is running, or the board predates both', () => {
    for (const a of [null, attention(), attention({ findings: [], goose: goose() })]) {
      expect(draw(a)).not.toContain('>Findings</h3>');
    }
  });
});
