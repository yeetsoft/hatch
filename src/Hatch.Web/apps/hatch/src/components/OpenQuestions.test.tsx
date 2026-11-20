import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { QuestionLevelControl, QuestionOptions, Waiting } from './OpenQuestions';
import type { AskedQuestion } from '../lib/questions';
import type { QuestionLevel, QuestionOption } from '../types';

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
  level: 'hold',
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
    <Waiting issueKey="HA-1" questions={questions} obviated={obviated} onAnswered={noop} onLeveled={noop} onError={noop} />,
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

describe('QuestionLevelControl', () => {
  const control = (level: QuestionLevel, over: { canRaise?: boolean; error?: string | null; busy?: boolean } = {}) =>
    renderToStaticMarkup(
      <QuestionLevelControl
        level={level}
        canRaise={over.canRaise ?? true}
        busy={over.busy ?? false}
        error={over.error ?? null}
        onPick={noop}
      />,
    );

  it.each([
    ['hold', 'nothing moves until this is answered'],
    ['build', 'built but not merged until this is answered'],
    ['merge', 'merges now; the answer is looked over after'],
  ] as const)('says what %s means and marks it as the one pressed', (level, words) => {
    const html = control(level);

    expect(html).toContain(words);
    expect(html).toContain('aria-label="Set level"');
    expect(count(html, 'aria-pressed="true"')).toBe(1);
    expect(count(html, 'aria-pressed="false"')).toBe(2);
    expect(html).toMatch(new RegExp(`aria-pressed="true"[^>]*disabled=""[^>]*>${level}<`));
  });

  it('draws the server\'s refusal beside the control', () => {
    const html = control('hold', { error: 'A question with no recommendation cannot be lowered.' });

    expect(html).toContain('<span class="text-danger">A question with no recommendation cannot be lowered.</span>');
  });

  it('draws no refusal when there is none', () => {
    expect(control('hold')).not.toContain('text-danger');
  });

  it('offers only hold where there is no recommendation, and says why', () => {
    const html = control('hold', { canRaise: false });

    expect(count(html, '<button')).toBe(1);
    expect(html).toContain('>hold<');
    expect(html).not.toContain('>build<');
    expect(html).not.toContain('>merge<');
    expect(html).toContain('offers no recommendation');
  });

  it('does not mention a recommendation where there is one', () => {
    expect(control('hold')).not.toContain('offers no recommendation');
  });

  it('disables every button while a change is in flight', () => {
    const html = control('build', { busy: true });

    expect(count(html, 'disabled=""')).toBe(3);
  });
});

describe('the card at each level', () => {
  const recommended = [option({ recommended: true })];

  it('says nothing is picked up at hold', () => {
    expect(waiting([question({ options: recommended })])).toContain('HA-1 will not be picked up again until these are answered.');
  });

  it('says it builds and waits in review at build', () => {
    const html = waiting([question({ options: recommended, level: 'build' })]);

    expect(html).toContain('HA-1 will be built on the recommendation and wait in review until these are answered.');
  });

  it('says it builds and merges at merge', () => {
    const html = waiting([question({ options: recommended, level: 'merge' })]);

    expect(html).toContain('HA-1 will be built and merged on the recommendation; these wait for a look back.');
  });

  it('follows the strictest question, not the loosest', () => {
    const html = waiting([
      question({ id: 1, options: recommended, level: 'merge' }),
      question({ id: 2, options: recommended, level: 'build' }),
    ]);

    expect(html).toContain('wait in review');
    expect(html).not.toContain('built and merged');
  });

  it('draws a control under each question', () => {
    const html = waiting([question({ id: 1, options: recommended, level: 'build' }), question({ id: 2 })]);

    expect(count(html, 'hatch-question-level-steps')).toBe(2);
  });

  it('draws no control and keeps its own sentence once the issue shipped', () => {
    const html = waiting([question({ options: recommended, level: 'merge' })], true);

    expect(html).not.toContain('hatch-question-level');
    expect(html).toContain('HA-1 shipped with these open.');
    expect(html).not.toContain('built and merged');
  });
});
