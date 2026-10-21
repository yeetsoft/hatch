import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ProjectEditDialog } from './ProjectEditDialog';
import type { Project } from '../types';

// canAdminister: false so the read-only summary paragraph renders - the
// editable branch isn't what's being proven here. The three new useLoaded
// calls (getPeople, getAssignees, getStatuses) fire inside useEffect, which
// renderToStaticMarkup never runs, so nothing calls fetch during this test -
// the same reason TrunkBuildBugDialog.test.tsx's "before the column fetch
// resolves" test works with no network stub.
function project(overrides: Partial<Project>): Project {
  return {
    id: 1,
    key: 'AER',
    name: 'Aerial',
    issueCount: 0,
    createdAt: '2026-01-01',
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
    runnerTags: [],
    ...overrides,
  };
}

function render(p: Project) {
  return renderToStaticMarkup(<ProjectEditDialog project={p} onClose={() => undefined} onSaved={() => undefined} />);
}

describe('ProjectEditDialog', () => {
  it('summarizes auto-heal as off when the switch is off', () => {
    const html = render(project({ autoHealBugEnabled: false }));

    expect(html).toContain('auto-heal off,');
  });

  it('summarizes auto-heal with its priority, pre-approval and column when the switch is on', () => {
    const html = render(
      project({
        autoHealBugEnabled: true,
        autoHealBugPriority: 'emergency',
        autoHealBugPreApproval: 'express',
        autoHealBugStatusId: null,
      }),
    );

    expect(html).toContain('files a emergency bug at express pre-approval in the automatic column,');
  });

  it('summarizes playbooks as written on the board when repoSourced is off', () => {
    const html = render(project({ repoSourced: false }));

    expect(html).toContain('playbooks written on the board.');
  });

  it('summarizes playbooks as sourced from the repository when repoSourced is on', () => {
    const html = render(project({ repoSourced: true }));

    expect(html).toContain('playbooks sourced from the repository.');
  });

  it('summarizes runner tags as "Any runner." when the list is empty', () => {
    const html = render(project({ runnerTags: [] }));

    expect(html).toContain('Any runner.');
  });

  it('summarizes runner tags as "Runners tagged X and Y." when the list is not empty', () => {
    const html = render(project({ runnerTags: ['macos', 'xcode'] }));

    expect(html).toContain('Runners tagged macos and xcode.');
  });

  it('shows an owner the runner tags textarea, one tag per line', () => {
    const html = render(project({ canAdminister: true, runnerTags: ['macos', 'xcode'] }));

    expect(html).toContain('macos\nxcode');
    expect(html).not.toContain('Any runner.');
  });
});
