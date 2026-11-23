import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { ReleaseFailure } from './IssuePage';
import type { IssueEvent } from '../types';

vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));
vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));

/* No DOM runner: ReleaseFailure is drawn from the event alone. */
const release = (payload: IssueEvent['payload']): IssueEvent => ({
  id: 1,
  actor: 'build-box',
  kind: 'claim_released',
  payload,
  at: '2026-11-22T10:00:00Z',
});

const draw = (event: IssueEvent) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <ReleaseFailure event={event} />
    </MemoryRouter>,
  );

describe('ReleaseFailure', () => {
  it('names the runner, the reason and what the tool said, with a link to the runner', () => {
    const html = draw(release({ failure: 'agent-signed-out', failureWhy: 'please log in' }));
    expect(html).toContain('build-box could not go on - its agent is signed out');
    expect(html).toContain('the tool said: please log in');
    expect(html).toContain('href="/runners/build-box" data-discover="true">build-box</a>');
  });

  it('draws a kind this build does not know as it came', () => {
    expect(draw(release({ failure: 'disk-full' }))).toContain('build-box hit a failure (disk-full)');
  });

  it('draws no said-line where the release kept no reason', () => {
    expect(draw(release({ failure: 'signing' }))).not.toContain('the tool said');
  });
});
