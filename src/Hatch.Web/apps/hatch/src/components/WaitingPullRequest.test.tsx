import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { WaitingPullRequestChip, WaitingPullRequestFact } from './WaitingPullRequest';
import type { WaitingPullRequest } from '../types';

const waiting: WaitingPullRequest = {
  title: 'Area: what changed',
  body: 'The body\n<script>alert(1)</script>',
  branch: 'ha-1-thing',
  sha: '2222222222222222222222222222222222222222',
  remote: 'forge.example/owner/repo',
  runner: 'a runner',
  why: 'the forge refused the push',
  at: '2026-11-22T12:00:00Z',
};

const chip = (url: string | null, w: WaitingPullRequest | null | undefined) =>
  renderToStaticMarkup(<WaitingPullRequestChip url={url} waiting={w} />);
const fact = (url: string | null, w: WaitingPullRequest | null | undefined) =>
  renderToStaticMarkup(<WaitingPullRequestFact url={url} waiting={w} />);

describe('WaitingPullRequest', () => {
  it('draws the chip with no url', () => {
    const html = chip(null, waiting);

    expect(html).toContain('hatch-chip');
    expect(html).toContain('waiting');
  });

  it('draws the sentence, who wrote it and why, and the title and body to read', () => {
    const html = fact(null, waiting);

    expect(html).toContain('Written, waiting to be opened');
    expect(html).toContain('a runner');
    expect(html).toContain('the forge refused the push');
    expect(html).toContain('<details>');
    expect(html).toContain('<summary>Area: what changed</summary>');
    expect(html).toContain('ha-1-thing');
    expect(html).toContain('222222222222');
    expect(html).toContain('forge.example/owner/repo');
  });

  it('draws the body as text, never as markup', () => {
    const html = fact(null, waiting);

    expect(html).toContain('&lt;script&gt;');
    expect(html).not.toContain('<script>');
  });

  it('carries no control that writes', () => {
    const html = fact(null, waiting);

    expect(html).not.toContain('<button');
    expect(html).not.toContain('<input');
    expect(html).not.toContain('<select');
  });

  it('is hidden once a url is recorded, and the url wins over a stale read', () => {
    expect(chip('https://forge.example/o/r/pull/1', waiting)).toBe('');
    expect(fact('https://forge.example/o/r/pull/1', waiting)).not.toContain('Written, waiting');
  });

  it.each([null, undefined])('is hidden when there is none (%s): no chip, and the dash in the fact', (none) => {
    expect(chip(null, none)).toBe('');
    expect(fact(null, none)).toContain('—');
    expect(fact(null, none)).not.toContain('Written, waiting');
  });
});
