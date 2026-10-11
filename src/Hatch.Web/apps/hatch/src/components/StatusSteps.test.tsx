import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { StatusSteps } from './StatusSteps';
import type { Status } from '../types';

const status = (id: number, sortOrder: number, opts: Partial<Status> = {}): Status => ({
  id,
  name: `status-${id}`,
  sortOrder,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  color: '#6b7280',
  ...opts,
});

const HERE = status(1, 0);
const OPEN = status(2, 1);

describe('StatusSteps', () => {
  it('disables a blocked column and carries the reason as its title', () => {
    const html = renderToStaticMarkup(
      <StatusSteps
        statuses={[HERE, OPEN]}
        statusId={HERE.id}
        onMove={() => {}}
        blocked={(target) => (target.id === OPEN.id ? 'Only an owner or approver may move work in here' : null)}
      />,
    );

    expect(html).toContain('title="Only an owner or approver may move work in here"');
    expect(html).toMatch(/status-2[^<]*<\/button>/);
    const openButton = html.match(/<button[^>]*>status-2<\/button>/)?.[0] ?? '';
    expect(openButton).toContain('disabled=""');
    expect(openButton).toContain('blocked');
  });

  it('leaves an unblocked column enabled, with no title', () => {
    const html = renderToStaticMarkup(
      <StatusSteps statuses={[HERE, OPEN]} statusId={HERE.id} onMove={() => {}} blocked={() => null} />,
    );

    const openButton = html.match(/<button[^>]*>status-2<\/button>/)?.[0] ?? '';
    expect(openButton).not.toContain('disabled');
    expect(openButton).not.toContain('title=');
    expect(openButton).not.toContain('blocked');
  });

  it('leaves every column enabled when blocked is omitted', () => {
    const html = renderToStaticMarkup(<StatusSteps statuses={[HERE, OPEN]} statusId={HERE.id} onMove={() => {}} />);

    const openButton = html.match(/<button[^>]*>status-2<\/button>/)?.[0] ?? '';
    expect(openButton).not.toContain('disabled');
  });
});
