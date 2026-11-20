import { describe, expect, it } from 'vitest';
import {
  askingCount,
  canRaise,
  fromComment,
  fromQuestion,
  LEVEL_WORDS,
  openQuestions,
  strictestLevel,
  threadQuestions,
  type AskedQuestion,
} from './questions';
import type { Comment, CommentKind, IssueCard, Question } from '../types';

let next = 1;

const comment = (kind: CommentKind, body: string, answersId: number | null = null): Comment => ({
  id: next++,
  author: kind === 'answer' ? 'operator' : 'hatch-agent',
  body,
  kind,
  answersId,
  options: null,
  createdAt: '2026-09-05T00:00:00Z',
  deliveredAt: null,
  deliveredTo: null,
  level: null,
  answeredAfter: false,
});

describe('threadQuestions', () => {
  it('finds nothing in a thread of ordinary comments', () => {
    expect(threadQuestions([comment('', 'sha abc123'), comment('', 'pushed')])).toEqual([]);
  });

  it('hangs an answer under the question it names', () => {
    const question = comment('question', 'per-node or global?');
    const answer = comment('answer', 'per-node', question.id);

    const threads = threadQuestions([question, comment('', 'unrelated'), answer]);

    expect(threads).toHaveLength(1);
    expect(threads[0].question.body).toBe('per-node or global?');
    expect(threads[0].answers.map((a) => a.body)).toEqual(['per-node']);
  });

  it('keeps every answer to one question, in the order they were given', () => {
    const question = comment('question', 'which disk?');
    const first = comment('answer', 'the 1200 GiB one', question.id);
    const second = comment('answer', 'on reflection, the reserved one', question.id);

    const [thread] = threadQuestions([question, first, second]);

    expect(thread.answers.map((a) => a.body)).toEqual(['the 1200 GiB one', 'on reflection, the reserved one']);
  });

  it('keeps two questions apart', () => {
    const one = comment('question', 'first');
    const two = comment('question', 'second');
    const answer = comment('answer', 'about the second', two.id);

    const threads = threadQuestions([one, two, answer]);

    expect(threads.map((t) => t.question.body)).toEqual(['first', 'second']);
    expect(threads[0].answers).toEqual([]);
    expect(threads[1].answers).toHaveLength(1);
  });

  /* An answer whose question is not in the list - a row hand-written, or a
     comment list that arrived truncated. It is dropped rather than promoted to
     a thread of its own: an answer with no question is not something the page
     can render, and inventing a question to hang it under would be a lie. */
  it('drops an answer pointing at a question that is not here', () => {
    expect(threadQuestions([comment('answer', 'to nothing', 9999)])).toEqual([]);
  });
});

describe('openQuestions', () => {
  it('is the questions with no answers', () => {
    const answered = comment('question', 'answered');
    const open = comment('question', 'open');

    const result = openQuestions([answered, comment('answer', 'yes', answered.id), open]);

    expect(result.map((t) => t.question.body)).toEqual(['open']);
  });

  it('is empty when everything has been decided', () => {
    const question = comment('question', 'decided');
    expect(openQuestions([question, comment('answer', 'yes', question.id)])).toEqual([]);
  });
});

const card = (over: Partial<IssueCard> = {}): IssueCard => ({
  key: 'AER-1',
  projectKey: 'AER',
  type: 'task',
  title: 'Renew the wildcard certificate',
  statusId: 1,
  rank: 1024,
  parentKey: null,
  readyAt: null,
  dueAt: null,
  openQuestions: 0,
  assignee: null,
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  preApproval: 'none',
  preApprovalOwn: 'none',
  preApprovalFrom: null,
  localModel: 'none',
  localModelOwn: 'none',
  localModelFrom: null,
  humanWork: 'agent',
  humanWorkOwn: 'inherit',
  humanWorkFrom: null,
  runnerTags: [],
  runnerTagsOwn: [],
  runnerTagsFrom: [],
  dangerousOwn: null,
  dangerous: null,
  dangerousFrom: null,
  omittedRunnersOwn: [],
  omittedRunners: [],
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
  createdBy: 'Britta Perry',
  projectPriority: 'normal',
  ...over,
});

describe('askingCount', () => {
  it('is zero for a column with nothing in it', () => {
    expect(askingCount([])).toBe(0);
  });

  it('is zero when nothing is owed an answer', () => {
    expect(askingCount([card(), card({ key: 'AER-2' }), card({ key: 'AER-3' })])).toBe(0);
  });

  it('finds the one asking among several that are not', () => {
    expect(
      askingCount([card(), card({ key: 'AER-2', openQuestions: 1 }), card({ key: 'AER-3' })]),
    ).toBe(1);
  });

  /* The count is of cards, not of questions: two questions on one card is one
     card a person has to go and look at. */
  it('counts cards rather than questions when several are asking', () => {
    expect(
      askingCount([
        card({ openQuestions: 2 }),
        card({ key: 'AER-2', openQuestions: 1 }),
        card({ key: 'AER-3' }),
      ]),
    ).toBe(2);
  });

  /* The helper never reads readyAt, and that is the point: the call site hands
     it a column's cards before the fold, so a card folded behind `+N waiting`
     is still counted. A question on work that cannot start yet is still owed to
     a person. */
  it('counts a card whose ready date has not arrived', () => {
    expect(askingCount([card({ readyAt: '2099-01-01', openQuestions: 1 })])).toBe(1);
  });
});

describe('fromComment', () => {
  it('carries the question and says askedAt for createdAt', () => {
    const q = comment('question', 'per-node or global?');

    expect(fromComment(q)).toEqual({
      id: q.id,
      body: 'per-node or global?',
      author: 'hatch-agent',
      askedAt: '2026-09-05T00:00:00Z',
      options: null,
      level: 'hold',
    });
  });

  it('carries the level a question was asked at', () => {
    expect(fromComment({ ...comment('question', 'which?'), level: 'merge' }).level).toBe('merge');
  });

  it('carries the options a question offered', () => {
    const options = [{ label: 'per-node', detail: 'one each', recommended: true }];
    const asked = fromComment({ ...comment('question', 'which?'), options });

    expect(asked.options).toEqual(options);
    expect('createdAt' in asked).toBe(false);
  });
});

const question = (over: Partial<Question> = {}): Question => ({
  id: next++,
  issueKey: 'AER-1',
  issueTitle: 'Renew the wildcard certificate',
  body: 'per-node or global?',
  askedBy: 'hatch-agent',
  askedAt: '2026-09-05T00:00:00Z',
  options: null,
  answers: [],
  level: 'hold',
  lookBack: false,
  ...over,
});

describe('fromQuestion', () => {
  it('carries the question and says askedBy for author', () => {
    const q = question();

    expect(fromQuestion(q)).toEqual({
      id: q.id,
      body: 'per-node or global?',
      author: 'hatch-agent',
      askedAt: '2026-09-05T00:00:00Z',
      options: null,
      level: 'hold',
    });
  });

  it('carries the level of the question', () => {
    expect(fromQuestion(question({ level: 'build' })).level).toBe('build');
  });

  it('carries the options a question offered', () => {
    const options = [{ label: 'per-node', detail: 'one each', recommended: true }];
    const asked = fromQuestion(question({ options }));

    expect(asked.options).toEqual(options);
    expect('issueKey' in asked).toBe(false);
  });
});

const asked = (over: Partial<AskedQuestion> = {}): AskedQuestion => ({
  id: next++,
  body: 'which?',
  author: 'hatch-agent',
  askedAt: '2026-09-05T00:00:00Z',
  options: null,
  level: 'hold',
  ...over,
});

describe('strictestLevel', () => {
  it('is hold where nothing is asked', () => {
    expect(strictestLevel([])).toBe('hold');
  });

  it('is the level of a lone question', () => {
    expect(strictestLevel([asked({ level: 'merge' })])).toBe('merge');
  });

  it('is the lowest of several, whatever order they come in', () => {
    expect(strictestLevel([asked({ level: 'merge' }), asked({ level: 'build' })])).toBe('build');
    expect(strictestLevel([asked({ level: 'merge' }), asked({ level: 'hold' }), asked({ level: 'build' })])).toBe('hold');
  });
});

describe('canRaise', () => {
  it('is false with no options, or none recommended', () => {
    expect(canRaise(asked())).toBe(false);
    expect(canRaise(asked({ options: [{ label: 'a', detail: null, recommended: false }] }))).toBe(false);
  });

  it('is true once an option is recommended', () => {
    expect(canRaise(asked({ options: [{ label: 'a', detail: null, recommended: true }] }))).toBe(true);
  });
});

describe('LEVEL_WORDS', () => {
  it('has a sentence for each level', () => {
    expect(Object.keys(LEVEL_WORDS)).toEqual(['hold', 'build', 'merge']);
  });
});
