import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ContributionBadge, ContributionMark } from './ContributionMark';
import type { ContributionRecord } from '../types';

const record = (over: Partial<ContributionRecord> = {}): ContributionRecord => ({
  pullRequestUrl: null,
  headBranch: null,
  authorLogin: 'someone',
  authorAssociation: 'NONE',
  filedAt: '2026-11-11T00:00:00Z',
  filedBy: 'runner',
  ...over,
});

describe('ContributionMark', () => {
  it('is a stroked shape named for a reader who cannot see it', () => {
    const html = renderToStaticMarkup(<ContributionMark />);
    expect(html).toContain('aria-label="contribution"');
    expect(html).toContain('<path');
    expect(html).not.toMatch(/fill="[^n]/);
  });
});

describe('ContributionBadge', () => {
  it('links the instance, as text and href only, where it is an http(s) address', () => {
    const html = renderToStaticMarkup(
      <ContributionBadge contribution={record({ originKey: 'AER-12', originInstance: 'https://hatch.example.org' })} />,
    );
    expect(html).toContain('AER-12');
    expect(html).toContain('<a href="https://hatch.example.org/"');
    expect(html).toContain('rel="noopener noreferrer"');
    expect(html).toContain('>hatch.example.org</a>');
  });

  it('draws no <a> inside a link, but a role=link span carrying the address', () => {
    const html = renderToStaticMarkup(
      <ContributionBadge
        inLink
        contribution={record({ originKey: 'AER-12', originInstance: 'https://hatch.example.org' })}
      />,
    );
    expect(html).not.toContain('<a');
    expect(html).toContain('role="link"');
    expect(html).toContain('title="https://hatch.example.org/"');
  });

  it('draws the instance as plain text where it is not an http(s) address', () => {
    const html = renderToStaticMarkup(
      <ContributionBadge contribution={record({ originKey: 'AER-12', originInstance: 'javascript:alert(1)' })} />,
    );
    expect(html).not.toContain('<a');
    expect(html).not.toContain('href');
  });

  it('names @login with no link where the record has no origin', () => {
    const html = renderToStaticMarkup(<ContributionBadge contribution={record()} />);
    expect(html).toContain('@someone');
    expect(html).not.toContain('<a');
  });
});
