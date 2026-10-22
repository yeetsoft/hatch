import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
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
  runnerTags: [],
  color: '#6b7280',
  ...opts,
});

const LOCK_REASON = 'Only an owner or approver may move work in here';

const render = (props: {
  status: Status;
  count: number;
  asking: number;
  doneHorizonHours: number | null;
}) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <ColumnHead {...props} />
    </MemoryRouter>,
  );

describe('ColumnHead', () => {
  it('carries the lock mark, with its title and accessible name, on a protected column', () => {
    const html = render({ status: status({ isProtected: true }), count: 3, asking: 0, doneHorizonHours: null });

    expect(html).toContain('hatch-column-lock');
    expect(html).toContain(`title="${LOCK_REASON}"`);
    expect(html).toContain(`aria-label="${LOCK_REASON}"`);
    expect(html).toContain('<svg');
  });

  it('carries neither the class nor the title on an unprotected column', () => {
    const html = render({ status: status({ isProtected: false }), count: 3, asking: 0, doneHorizonHours: null });

    expect(html).not.toContain('hatch-column-lock');
    expect(html).not.toContain(LOCK_REASON);
  });

  it('still draws the asking badge beside the lock mark', () => {
    const html = render({ status: status({ isProtected: true }), count: 3, asking: 2, doneHorizonHours: null });

    expect(html).toContain('hatch-column-lock');
    expect(html).toContain('hatch-column-asking');
    expect(html).toContain('?2');
  });

  it('draws the done horizon on a terminal column that has one', () => {
    const html = render({ status: status({ isTerminal: true }), count: 5, asking: 0, doneHorizonHours: 48 });

    expect(html).toContain('hatch-column-horizon');
    expect(html).toContain('last 2 days');
    expect(html).toContain('href="/search?statusId=1"');
  });

  it('draws nothing when a terminal column has no horizon', () => {
    const html = render({ status: status({ isTerminal: true }), count: 5, asking: 0, doneHorizonHours: null });

    expect(html).not.toContain('hatch-column-horizon');
  });

  it('never draws the horizon on a non-terminal column, even if given one', () => {
    const html = render({ status: status({ isTerminal: false }), count: 5, asking: 0, doneHorizonHours: 48 });

    expect(html).not.toContain('hatch-column-horizon');
  });

  it('draws the runner tags small under the name when the column requires them', () => {
    const html = render({
      status: status({ runnerTags: ['breakdown', 'macos'] }),
      count: 3,
      asking: 0,
      doneHorizonHours: null,
    });

    expect(html).toContain('hatch-column-title');
    expect(html).toContain('hatch-column-tags');
    expect(html).toContain('breakdown, macos');
    expect(html).toContain('hatch-column-name');
  });

  it('draws neither the tags span nor its wrapper when the column requires none', () => {
    const html = render({ status: status({ runnerTags: [] }), count: 3, asking: 0, doneHorizonHours: null });

    expect(html).not.toContain('hatch-column-tags');
    expect(html).not.toContain('hatch-column-title');
  });

  it('still draws the lock and the asking badge on a tagged protected column', () => {
    const html = render({
      status: status({ runnerTags: ['breakdown'], isProtected: true }),
      count: 3,
      asking: 2,
      doneHorizonHours: null,
    });

    expect(html).toContain('hatch-column-lock');
    expect(html).toContain('hatch-column-asking');
    expect(html).toContain('?2');
  });
});
