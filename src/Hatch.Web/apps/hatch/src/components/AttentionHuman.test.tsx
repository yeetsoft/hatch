import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { AttentionHuman } from './AttentionHuman';
import { questionEmptyWords, reviewEmptyWords, trunkBuildEmptyWords } from '../lib/attention';

/* No DOM here, so this is structural - react-dom/server, the way
   Confirmations.test.tsx reads. What it pins is AC1's "same order, same
   empty wordings" against a refactor that might reorder the extraction by
   accident. */
const noop = () => Promise.resolve();

beforeEach(() => vi.stubGlobal('window', { location: { pathname: '/apps/hatch/' } }));
afterEach(() => vi.unstubAllGlobals());

describe('AttentionHuman', () => {
  it('draws trunk builds, then pull requests, then questions, each with the panel’s own empty wording', () => {
    const html = renderToStaticMarkup(<AttentionHuman attention={null} now={new Date()} reload={noop} />);

    const headings = [...html.matchAll(/<h3 class="hatch-attention-heading">([^<]*)<\/h3>/g)].map((m) => m[1]);
    expect(headings).toEqual(['Trunk builds that fail', 'Pull requests to review', 'Questions to answer']);

    const trunkAt = html.indexOf(trunkBuildEmptyWords());
    const reviewAt = html.indexOf(reviewEmptyWords(null));
    const questionAt = html.indexOf(questionEmptyWords());

    expect(trunkAt).toBeGreaterThan(-1);
    expect(trunkAt).toBeLessThan(reviewAt);
    expect(reviewAt).toBeLessThan(questionAt);
  });

  it('draws no outer heading and no .hatch-attention-group wrapper of its own', () => {
    const html = renderToStaticMarkup(<AttentionHuman attention={null} now={new Date()} reload={noop} />);

    expect(html).not.toContain('hatch-attention-group');
    expect(html).not.toContain('<h2');
  });
});
