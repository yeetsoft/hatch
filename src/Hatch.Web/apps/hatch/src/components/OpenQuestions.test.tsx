import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { QuestionOptions, Waiting } from './OpenQuestions';
import type { AskedQuestion } from '../lib/questions';
import type { QuestionOption } from '../types';

// renderMarkdown's DOMPurify pair is imported at module load and needs a real
// DOM, which this workspace's tests do not have (plain Node, no jsdom) - the
// same stub ProseClamp.test.tsx uses. The identity stub still lets these tests
// check what the card decided to show.
vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));

const question = (over: Partial<AskedQuestion> = {}): AskedQuestion => ({
  id: 1,
  body: 'Which?',
  author: 'runner',
  askedAt: '2026-11-07T00:00:00Z',
  options: null,
  ...over,
});
const option = (over: Partial<QuestionOption> = {}): QuestionOption => ({
  label: 'alpha',
  detail: null,
  recommended: false,
  ...over,
});
const noop = () => undefined;

const two = [question({ id: 1 }), question({ id: 2 })];
const waiting = (questions: AskedQuestion[], obviated = false) =>
  renderToStaticMarkup(
    <Waiting issueKey="HA-1" questions={questions} obviated={obviated} onAnswered={noop} onError={noop} />,
  );
const count = (html: string, needle: string) => html.split(needle).length - 1;

describe('OpenQuestions', () => {
  it('titles the card "Waiting on you" and marks it waiting while the issue is live', () => {
    const html = waiting(two);

    expect(html).toContain('Waiting on you (2)');
    expect(html).toContain('hatch-waiting');
  });

  it('titles the card "Asked" and drops the waiting mark once the issue is obviated', () => {
    const html = waiting(two, true);

    expect(html).toContain('Asked (2)');
    expect(html).not.toContain('hatch-waiting');
    expect(html).not.toContain('Waiting on you');
  });

  it('draws nothing when there are no questions', () => {
    expect(waiting([])).toBe('');
  });

  it('marks the recommended option, and only that one', () => {
    const html = renderToStaticMarkup(
      <QuestionOptions options={[option({ label: 'alpha', recommended: true }), option({ label: 'beta' })]} onChoose={noop} />,
    );

    expect(html).toContain('alpha<span class="hatch-option-recommended">recommended</span>');
    expect(count(html, 'hatch-option-recommended')).toBe(1);
  });

  it('presses the chosen option and no other', () => {
    const html = renderToStaticMarkup(
      <QuestionOptions options={[option({ label: 'alpha' }), option({ label: 'beta' })]} chosen="alpha" onChoose={noop} />,
    );

    expect(html).toContain('<button');
    expect(count(html, 'aria-pressed="true"')).toBe(1);
    expect(count(html, 'aria-pressed="false"')).toBe(1);
    expect(html).toMatch(/aria-pressed="true"[^>]*>(?:(?!<\/button>).)*alpha/);
  });

  it('draws the options as a static record, with no buttons, when nothing can choose them', () => {
    const html = renderToStaticMarkup(
      <QuestionOptions options={[option({ label: 'alpha' }), option({ label: 'beta' })]} />,
    );

    expect(html).toContain('hatch-options static');
    expect(html).not.toContain('<button');
    expect(html).not.toContain('aria-pressed');
  });

  it('words the answer box for whether the question offered choices', () => {
    const withOptions = waiting([question({ options: [option()] })]);
    const without = waiting([question({ options: null })]);

    expect(withOptions).toContain('Or say something else.');
    expect(without).toContain('The decision, in a sentence.');
    expect(without).not.toContain('Or say something else.');
  });

  it('draws an option detail only when it has one', () => {
    const withDetail = renderToStaticMarkup(
      <QuestionOptions options={[option({ label: 'alpha', detail: 'costs a day' })]} />,
    );
    const without = renderToStaticMarkup(<QuestionOptions options={[option({ label: 'alpha', detail: null })]} />);

    expect(withDetail).toContain('<span class="hatch-option-detail">costs a day</span>');
    expect(without).not.toContain('hatch-option-detail');
  });
});
