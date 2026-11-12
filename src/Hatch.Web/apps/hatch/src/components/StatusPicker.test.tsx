import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { StatusPicker } from './StatusPicker';
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
  runnerTags: [],
  color: '#6b7280',
  ...opts,
});

const HERE = status(1, 0);
const OPEN = status(2, 1);

const render = (props: Partial<Parameters<typeof StatusPicker>[0]> = {}) =>
  renderToStaticMarkup(
    <StatusPicker issueKey="HA-1" status={HERE} statuses={[HERE, OPEN]} onMove={() => {}} {...props} />,
  );

describe('StatusPicker', () => {
  it('draws a small, folded pill by default', () => {
    const html = render();

    expect(html).toContain('hatch-status-pill-sm');
    expect(html).toContain('aria-expanded="false"');
    expect(html).not.toContain('hatch-status-band');
    expect(html).not.toContain('hatch-status-step');
  });

  it('draws the large pill, and not the small one, at size lg', () => {
    const html = render({ size: 'lg' });

    expect(html).toContain('hatch-status-pill-lg');
    expect(html).not.toContain('hatch-status-pill-sm');
  });

  it('is pressable when busy is omitted', () => {
    const html = render();

    expect(html).toContain('aria-busy="false"');
    expect(html).not.toContain('disabled');
  });

  it('is disabled while a move is out', () => {
    const html = render({ busy: true });

    expect(html).toContain('aria-busy="true"');
    expect(html).toContain('disabled=""');
  });
});
