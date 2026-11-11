import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { AttentionPanel } from './AttentionPanel';
import { reviewEmptyWords } from '../lib/attention';
import type { Attention, Conflict, ExhaustedRunner } from '../types';

/* No DOM here, so this is structural - react-dom/server, the way
   AttentionHuman.test.tsx reads - and a press cannot be made: only the
   initial markup is asserted. */
const noop = () => Promise.resolve();

vi.mock('./TrunkBuildBugDialog', () => ({
  TrunkBuildBugDialog: () => null,
}));

const attention = (over: Partial<Attention> = {}): Attention => ({
  reviews: [],
  inReviewWithoutPullRequest: 0,
  questions: [],
  conflicts: [],
  reviewsHeldBack: 0,
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

const exhausted = (over: Partial<ExhaustedRunner> = {}): ExhaustedRunner => ({
  name: 'runner-a',
  where: null,
  exhaustedUntil: new Date(Date.now() + 3_600_000).toISOString(),
  ...over,
});

const render = (a: Attention, startOpen?: boolean): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <AttentionPanel attention={a} now={new Date()} reload={noop} startOpen={startOpen} />
    </MemoryRouter>,
  );

const buttons = (html: string, expanded: boolean): number =>
  (html.match(new RegExp(`aria-expanded="${expanded}"`, 'g')) ?? []).length;

const foldButton = (title: string, expanded: boolean): RegExp =>
  new RegExp(`<button[^>]*aria-expanded="${expanded}"[^>]*>${title}</button>`);

const plainH2 = (title: string): string => `<h2 class="hatch-attention-group-heading">${title}</h2>`;
const plainH3 = (title: string): string => `<h3 class="hatch-attention-heading">${title}</h3>`;

const sections = ['Trunk builds that fail', 'Pull requests to review', 'Branches that conflict', 'Branch builds that fail'];

describe('AttentionPanel', () => {
  it('draws a quiet board as the Human and Agent buttons and nothing else', () => {
    const html = render(attention());

    expect(buttons(html, false)).toBe(2);
    expect(html).toMatch(foldButton('Human', false));
    expect(html).toMatch(foldButton('Agent', false));
    for (const title of [...sections, 'Out of usage']) expect(html).not.toContain(title);
  });

  it('draws Out of usage beside the two group buttons when a runner is out', () => {
    const html = render(attention({ exhaustedRunners: [exhausted()] }));

    expect(buttons(html, false)).toBe(2);
    expect(html).toContain(plainH3('Out of usage'));
    expect(html).toContain('runner-a');
  });

  it('draws Agent plain, its conflicts open and its other section folded, when a branch conflicts', () => {
    const html = render(attention({ conflicts: [conflict()] }));

    expect(html).toContain(plainH2('Agent'));
    expect(html).toContain(plainH3('Branches that conflict'));
    expect(html).toContain('AER-14');
    expect(html).toMatch(foldButton('Branch builds that fail', false));
    expect(html).toMatch(foldButton('Human', false));
    expect(buttons(html, false)).toBe(2);
  });

  it('draws Human plain with the review sentence when an issue sits in review with no pull request', () => {
    const a = attention({ inReviewWithoutPullRequest: 3 });
    const html = render(a);

    expect(html).toContain(plainH2('Human'));
    expect(html).toContain(plainH3('Pull requests to review'));
    expect(html).toContain(reviewEmptyWords(a));
    expect(html).toMatch(foldButton('Agent', false));
  });

  it('opens every group and section at once under startOpen', () => {
    const html = render(attention(), true);

    expect(buttons(html, false)).toBe(0);
    expect(html).toMatch(foldButton('Human', true));
    expect(html).toMatch(foldButton('Agent', true));
    for (const title of sections) expect(html).toMatch(foldButton(title, true));
  });
});
