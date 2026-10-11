import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ColumnHead } from './ColumnHead';
import type { Status } from '../types';

const status = (opts: Partial<Status> = {}): Status => ({
  id: 1,
  name: 'In Review',
  sortOrder: 0,
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

const LOCK_REASON = 'Only an owner or approver may move work in here';

describe('ColumnHead', () => {
  it('carries the lock mark, with its title and accessible name, on a protected column', () => {
    const html = renderToStaticMarkup(<ColumnHead status={status({ isProtected: true })} count={3} asking={0} />);

    expect(html).toContain('hatch-column-lock');
    expect(html).toContain(`title="${LOCK_REASON}"`);
    expect(html).toContain(`aria-label="${LOCK_REASON}"`);
    expect(html).toContain('<svg');
  });

  it('carries neither the class nor the title on an unprotected column', () => {
    const html = renderToStaticMarkup(<ColumnHead status={status({ isProtected: false })} count={3} asking={0} />);

    expect(html).not.toContain('hatch-column-lock');
    expect(html).not.toContain(LOCK_REASON);
  });

  it('still draws the asking badge beside the lock mark', () => {
    const html = renderToStaticMarkup(<ColumnHead status={status({ isProtected: true })} count={3} asking={2} />);

    expect(html).toContain('hatch-column-lock');
    expect(html).toContain('hatch-column-asking');
    expect(html).toContain('?2');
  });
});
