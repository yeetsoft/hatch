import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { Project } from '../types';
import { ImportProjectField } from './ImportPage';

/* No DOM runner: the page's editor and logger reach for `window` on import, and
   the field is rendered from props alone. */
vi.mock('../components/MarkdownEditor', () => ({ MarkdownEditor: () => null }));
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const noop = () => {};

function project(overrides: Partial<Project>): Project {
  return {
    id: 1,
    key: 'HA',
    name: 'Hatch',
    issueCount: 0,
    createdAt: '2026-01-01T00:00:00Z',
    color: null,
    icon: null,
    logoUpdatedAt: null,
    repositories: [],
    members: [],
    canApprove: false,
    canAdminister: false,
    landing: 'pull-request',
    checksRequired: true,
    onePullRequest: false,
    autoHealBugEnabled: false,
    autoHealBugPriority: 'emergency',
    autoHealBugPreApproval: 'express',
    autoHealBugStatusId: null,
    repoSourced: false,
    repoSourcedBy: null,
    repoSourcedAt: null,
    bindsTracker: false,
    bindsTrackerBy: null,
    bindsTrackerAt: null,
    reflectionCadenceDays: null,
    reflectionLastPeriodStart: null,
    reflectionNextDueAt: null,
    storiesOnEpics: false,
    checkApprovesMerge: false,
    checkApprovesMergeBy: null,
    checkApprovesMergeAt: null,
    ...overrides,
  };
}

const AER = project({ id: 2, key: 'AER', name: 'Aerie' });
const FIL = project({ id: 3, key: 'FIL', name: 'Fileroom' });

describe('ImportProjectField', () => {
  it('offers the list it is given and nothing else, beside the placeholder', () => {
    const html = renderToStaticMarkup(<ImportProjectField projects={[AER, FIL]} value={null} onChange={noop} />);

    expect(html.match(/<option/g)).toHaveLength(3);
    expect(html).toContain('AER — Aerie');
    expect(html).toContain('FIL — Fileroom');
    expect(html).not.toContain('Hatch');
    // Only the placeholder is selected when nothing is chosen.
    expect(html.match(/<option[^>]*selected/g)).toHaveLength(1);
    expect(html).toMatch(/<option value="" selected/);
  });

  it('selects the option matching value', () => {
    const html = renderToStaticMarkup(<ImportProjectField projects={[AER, FIL]} value={3} onChange={noop} />);

    expect(html).toMatch(/<option[^>]*value="3"[^>]*selected|<option[^>]*selected[^>]*value="3"/);
    expect(html).not.toMatch(/<option[^>]*value="2"[^>]*selected|<option[^>]*selected[^>]*value="2"/);
  });

  it('renders a sole project selected when value is its id', () => {
    const html = renderToStaticMarkup(<ImportProjectField projects={[AER]} value={2} onChange={noop} />);

    expect(html.match(/<option/g)).toHaveLength(2);
    expect(html).toMatch(/<option[^>]*value="2"[^>]*selected|<option[^>]*selected[^>]*value="2"/);
  });
});
