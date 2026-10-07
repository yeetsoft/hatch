import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ClaimBadge } from './ClaimBadge';
import type { IssueClaim } from '../types';

const claim = (over: Partial<IssueClaim> = {}): IssueClaim => ({
  claimedBy: 'hatch',
  runner: 'Jeff Winger',
  claimedAt: new Date().toISOString(),
  heartbeatAt: new Date().toISOString(),
  chatter: null,
  chatterAt: null,
  ttlSeconds: 300,
  ...over,
});

describe('ClaimBadge', () => {
  it('draws the robot head and the fresh class on a heartbeat just heard from', () => {
    const html = renderToStaticMarkup(<ClaimBadge claim={claim()} />);

    expect(html).toContain('hatch-card-claim hatch-card-claim-fresh');
    expect(html).toContain('<svg');
    expect(html).toContain('hatch-robot-head');
  });

  it('draws the quiet class on a holder that has gone quiet on its lease', () => {
    const html = renderToStaticMarkup(
      <ClaimBadge claim={claim({ heartbeatAt: new Date(0).toISOString(), ttlSeconds: 300 })} />,
    );

    expect(html).toContain('hatch-card-claim hatch-card-claim-quiet');
  });

  it('draws nothing where there is no claim', () => {
    expect(renderToStaticMarkup(<ClaimBadge claim={null} />)).toBe('');
  });

  it('carries the sentence on the hover and the accessible name', () => {
    const html = renderToStaticMarkup(<ClaimBadge claim={claim({ claimedBy: 'Ada', runner: 'Jeff Winger' })} />);

    expect(html).toContain('title="Jeff Winger is working this, for Ada, last heard from just now"');
    expect(html).toContain('aria-label="Jeff Winger is working this, for Ada, last heard from just now"');
  });
});
