import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
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
    runnerTags: [],
    ...overrides,
  };
}

function render(p: Project) {
  return renderToStaticMarkup(
    <MemoryRouter>
      <ProjectEditDialog project={p} onClose={() => undefined} onSaved={() => undefined} />
    </MemoryRouter>,
  );
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

    expect(html).toContain('playbooks written on the board,');
  });

  it('summarizes playbooks as sourced from the repository when repoSourced is on', () => {
    const html = render(project({ repoSourced: true }));

    expect(html).toContain('playbooks sourced from the repository,');
  });

  it('summarizes runner tags as "Any runner." when the list is empty', () => {
    const html = render(project({ runnerTags: [] }));

    expect(html).toContain('Any runner.');
  });

  it('summarizes runner tags as "Runners tagged X and Y." when the list is not empty', () => {
    const html = render(project({ runnerTags: ['macos', 'xcode'] }));

    expect(html).toContain('Runners tagged macos and xcode.');
  });

  it('shows an owner the binds-tracker checkbox, disabled while the project binds no repository', () => {
    const html = render(project({ canAdminister: true, repositories: [] }));

    expect(html).toContain("This project binds the tracker&#x27;s own repository");
    expect(html).toMatch(/<input type="checkbox" disabled=""[^>]*\/>This project binds the tracker/);
  });

  it('shows an owner who set the flag, and when', () => {
    const html = render(
      project({ canAdminister: true, bindsTracker: true, bindsTrackerBy: 'Nathan', bindsTrackerAt: '2026-03-09T00:00:00Z' }),
    );

    expect(html).toContain(`set by Nathan on ${new Date('2026-03-09T00:00:00Z').toLocaleDateString()}`);
  });

  it('shows anyone else a sentence, and no control, where the project binds the tracker', () => {
    const html = render(
      project({ canAdminister: false, bindsTracker: true, bindsTrackerBy: 'Nathan', bindsTrackerAt: '2026-03-09T00:00:00Z' }),
    );

    expect(html).toContain('Binds the tracker&#x27;s own repository');
    expect(html).toContain('set by Nathan');
    expect(html).not.toContain('This project binds the tracker');
  });

  it('shows anyone else nothing about the tracker where the project does not bind it', () => {
    const html = render(project({ canAdminister: false, bindsTracker: false }));

    expect(html).not.toContain('Binds the tracker');
  });

  it('shows an owner the Board heading and the stories-on-epics checkbox', () => {
    const html = render(project({ canAdminister: true }));

    expect(html).toContain('>Board</h2>');
    expect(html).toContain('Show stories on epics, not the board');
  });

  it('shows anyone else a sentence, and no control, where the project shows stories on epics', () => {
    const html = render(project({ canAdminister: false, storiesOnEpics: true }));

    expect(html).toContain('Stories shown on their epics.');
    expect(html).not.toContain('Show stories on epics, not the board');
  });

  it('shows anyone else a sentence where the project shows stories on the board', () => {
    const html = render(project({ canAdminister: false, storiesOnEpics: false }));

    expect(html).toContain('Stories shown on the board.');
    expect(html).not.toContain('Show stories on epics, not the board');
  });

  it('shows an owner the runner tags textarea, one tag per line', () => {
    const html = render(project({ canAdminister: true, runnerTags: ['macos', 'xcode'] }));

    expect(html).toContain('macos\nxcode');
    expect(html).not.toContain('Any runner.');
  });

  it('shows an owner the cadence box holding the days, and when the next reflection is due', () => {
    const nextDueAt = '2026-03-09T00:00:00Z';
    const html = render(
      project({ canAdminister: true, reflectionCadenceDays: 5, reflectionLastPeriodStart: '2026-03-04T00:00:00Z', reflectionNextDueAt: nextDueAt }),
    );

    expect(html).toContain('Reflects every');
    expect(html).toContain('value="5"');
    expect(html).toContain(`Next due ${new Date(nextDueAt).toLocaleDateString()}`);
  });

  it('shows an owner a blank cadence box, with no due hint, where the cadence is off', () => {
    const html = render(project({ canAdminister: true, reflectionCadenceDays: null }));

    expect(html).toContain('Reflects every');
    expect(html).not.toContain('Next due');
  });

  it('does not show a person who cannot administer the cadence box', () => {
    const html = render(project({ canAdminister: false, reflectionCadenceDays: 5, reflectionNextDueAt: '2026-03-09T00:00:00Z' }));

    expect(html).not.toContain('Reflects every');
  });

  it('shows an owner a Reflect now button beside the cadence box', () => {
    const html = render(project({ canAdminister: true }));

    expect(html).toContain('Reflect now');
  });

  it('does not show a person who cannot administer the Reflect now button', () => {
    const html = render(project({ canAdminister: false }));

    expect(html).not.toContain('Reflect now');
  });

  it('summarizes the cadence as never reflecting where it is off', () => {
    const html = render(project({ reflectionCadenceDays: null }));

    expect(html).toContain('playbooks written on the board, never reflects.');
  });

  it('summarizes the cadence and the next due date where one is set', () => {
    const nextDueAt = '2026-03-09T00:00:00Z';
    const html = render(project({ reflectionCadenceDays: 5, reflectionNextDueAt: nextDueAt }));

    expect(html).toContain(`reflects every 5 day(s), next due ${new Date(nextDueAt).toLocaleDateString()}.`);
  });
});
