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
    noCodeCloses: false,
    noCodeClosesBy: null,
    noCodeClosesAt: null,
    runnersWorkContributions: false,
    runnersWorkContributionsBy: null,
    runnersWorkContributionsAt: null,
    onePullRequestPolicy: 'optional',
    onePullRequestPolicyBy: null,
    onePullRequestPolicyAt: null,
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

  // The dependency-files load is a useEffect renderToStaticMarkup never runs, so
  // only this "nothing loaded yet" state is reachable here; parsed, invalid and
  // ambiguous are proven by lib/dependencyFiles.test.ts.
  it('says no runner has read the declaration beside a bound repository', () => {
    const html = render(
      project({
        canAdminister: true,
        repositories: [{ remote: 'git@forge.example:owner/repo.git', canonical: 'forge.example/owner/repo', baseBranch: null }],
      }),
    );

    expect(html).toContain('no runner has read its declaration yet');
  });

  it('hints each reorder arrow in the words its label carries', () => {
    const remote = 'git@forge.example:owner/repo.git';
    const html = render(
      project({ canAdminister: true, repositories: [{ remote, canonical: 'forge.example/owner/repo', baseBranch: null }] }),
    );

    for (const way of ['up', 'down']) {
      expect(html).toContain(`aria-label="Move ${remote} ${way}"`);
      expect(html).toContain(`title="Move ${remote} ${way}"`);
    }
  });

  it('says nothing about a declaration where the project binds no repository', () => {
    const html = render(project({ canAdminister: true, repositories: [] }));

    expect(html).not.toContain('no runner has read its declaration yet');
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

  it('shows an owner both one-pull-request policy radios and the hint, optional checked for an optional project', () => {
    const html = render(project({ canAdminister: true, onePullRequestPolicy: 'optional' }));

    expect(html).toContain('Stories may set their own, either way');
    expect(html).toContain('Every story follows the project');
    expect(html).toContain('no story or bug may be given a setting of its own');
    expect(html).toMatch(/<input type="radio"[^>]*checked=""[^>]*\/>Stories may set their own, either way/);
  });

  it('shows an owner enforced checked for an enforced project', () => {
    const html = render(project({ canAdminister: true, onePullRequestPolicy: 'enforced' }));

    expect(html).toMatch(/<input type="radio"[^>]*checked=""[^>]*\/>Every story follows the project/);
  });

  it('shows an owner who set the one-pull-request policy, and when', () => {
    const html = render(
      project({
        canAdminister: true,
        onePullRequestPolicyBy: 'Dana',
        onePullRequestPolicyAt: '2026-03-09T00:00:00Z',
      }),
    );

    expect(html).toContain(`set by Dana on ${new Date('2026-03-09T00:00:00Z').toLocaleDateString()}`);
  });

  it('shows an owner no set-by line when the one-pull-request policy has never been written', () => {
    const html = render(project({ canAdminister: true, onePullRequestPolicyBy: null, onePullRequestPolicyAt: null }));

    expect(html).not.toContain('set by');
  });

  it('shows anyone else the one-pull-request policy worked into the summary sentence, and no radios', () => {
    const optional = render(project({ canAdminister: false, onePullRequest: true, onePullRequestPolicy: 'optional' }));
    expect(optional).toContain('one pull request per story, which a story may override');

    const enforced = render(project({ canAdminister: false, onePullRequest: false, onePullRequestPolicy: 'enforced' }));
    expect(enforced).toContain('one pull request per task, which every story follows');

    expect(optional).not.toContain('Stories may set their own, either way');
    expect(optional).not.toContain('Every story follows the project');
  });

  it('shows anyone else the one-pull-request policy audit sentence when set', () => {
    const html = render(
      project({
        canAdminister: false,
        onePullRequestPolicyBy: 'Dana',
        onePullRequestPolicyAt: '2026-03-09T00:00:00Z',
      }),
    );

    expect(html).toContain(
      `The one-pull-request policy was set by Dana on ${new Date('2026-03-09T00:00:00Z').toLocaleDateString()}.`,
    );
  });

  it('shows an owner the check-approves-merge checkbox and its hint', () => {
    const html = render(project({ canAdminister: true }));

    expect(html).toContain('A passing check approves the merge');
    expect(html).toContain("A check playbook&#x27;s pass out of the review column");
  });

  it('shows an owner who set the check-approves-merge flag, and when', () => {
    const html = render(
      project({
        canAdminister: true,
        checkApprovesMerge: true,
        checkApprovesMergeBy: 'Nathan',
        checkApprovesMergeAt: '2026-03-09T00:00:00Z',
      }),
    );

    expect(html).toContain(`set by Nathan on ${new Date('2026-03-09T00:00:00Z').toLocaleDateString()}`);
  });

  it('shows an owner no set-by line when the check-approves-merge flag has never been written', () => {
    const html = render(project({ canAdminister: true, checkApprovesMergeBy: null, checkApprovesMergeAt: null }));

    expect(html).not.toContain('set by');
  });

  it('shows anyone else a sentence, and no control, where a passing check approves the merge', () => {
    const html = render(project({ canAdminister: false, checkApprovesMerge: true }));

    expect(html).toContain('A passing check approves the merge');
    expect(html).not.toContain("A check playbook&#x27;s pass out of the review column");
  });

  it('shows anyone else a sentence where a passing check does not approve the merge', () => {
    const html = render(project({ canAdminister: false, checkApprovesMerge: false }));

    expect(html).toContain('A passing check does not approve the merge');
  });

  it('shows an owner the no-code-closes checkbox and its hint', () => {
    const html = render(project({ canAdminister: true }));

    expect(html).toContain('A no-code declaration closes the ticket');
    expect(html).toContain('A ticket that declared it finished with no code is carried');
  });

  it('shows an owner who set the no-code-closes flag, and when', () => {
    const html = render(
      project({
        canAdminister: true,
        noCodeCloses: true,
        noCodeClosesBy: 'Dana',
        noCodeClosesAt: '2026-03-09T00:00:00Z',
      }),
    );

    expect(html).toContain(`set by Dana on ${new Date('2026-03-09T00:00:00Z').toLocaleDateString()}`);
  });

  it('shows an owner no set-by line when the no-code-closes flag has never been written', () => {
    const html = render(project({ canAdminister: true, noCodeClosesBy: null, noCodeClosesAt: null }));

    expect(html).not.toContain('set by');
  });

  it('shows anyone else a sentence, and no control, where a no-code declaration closes the ticket', () => {
    const html = render(project({ canAdminister: false, noCodeCloses: true }));

    expect(html).toContain('A no-code declaration closes the ticket');
    expect(html).not.toContain('A ticket that declared it finished with no code is carried');
  });

  it('shows anyone else a sentence where a no-code declaration does not close the ticket', () => {
    const html = render(project({ canAdminister: false, noCodeCloses: false }));

    expect(html).toContain('A no-code declaration does not close the ticket');
    expect(html).not.toContain('A ticket that declared it finished with no code is carried');
  });

  it('shows an owner the runners-work-contributions checkbox and its hint', () => {
    const html = render(project({ canAdminister: true }));

    expect(html).toContain('Runners work contributions');
    expect(html).toContain('Lets runners resolve conflicts and fix failing builds on a contribution');
  });

  it('shows an owner who set the runners-work-contributions flag, and when', () => {
    const html = render(
      project({
        canAdminister: true,
        runnersWorkContributions: true,
        runnersWorkContributionsBy: 'Dana',
        runnersWorkContributionsAt: '2026-03-09T00:00:00Z',
      }),
    );

    expect(html).toContain(`set by Dana on ${new Date('2026-03-09T00:00:00Z').toLocaleDateString()}`);
  });

  it('shows an owner no set-by line when the runners-work-contributions flag has never been written', () => {
    const html = render(
      project({ canAdminister: true, runnersWorkContributionsBy: null, runnersWorkContributionsAt: null }),
    );

    expect(html).not.toContain('set by');
  });

  it('shows anyone else a sentence, and no control, where runners work contributions', () => {
    const html = render(project({ canAdminister: false, runnersWorkContributions: true }));

    expect(html).toContain('Runners work contributions');
    expect(html).not.toContain('Lets runners resolve conflicts and fix failing builds');
  });

  it('shows anyone else a sentence where runners do not work contributions', () => {
    const html = render(project({ canAdminister: false, runnersWorkContributions: false }));

    expect(html).toContain('Runners do not work contributions');
    expect(html).not.toContain('Lets runners resolve conflicts and fix failing builds');
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

  it('tells an owner a cadence will wait for a person while a no-code declaration does not close the ticket', () => {
    const html = render(project({ canAdminister: true, reflectionCadenceDays: 5, noCodeCloses: false }));

    expect(html).toContain('will wait in review for a person');
    expect(html).toContain('A no-code declaration closes the ticket');
  });

  it('does not tell an owner that where a no-code declaration closes the ticket', () => {
    const html = render(project({ canAdminister: true, reflectionCadenceDays: 5, noCodeCloses: true }));

    expect(html).not.toContain('will wait in review for a person');
  });

  it('does not tell an owner that where no cadence is set', () => {
    const html = render(project({ canAdminister: true, reflectionCadenceDays: null, noCodeCloses: false }));

    expect(html).not.toContain('will wait in review for a person');
  });

  it('does not tell a person who cannot administer the cadence box that', () => {
    const html = render(project({ canAdminister: false, reflectionCadenceDays: 5, noCodeCloses: false }));

    expect(html).not.toContain('will wait in review for a person');
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

  describe('the auto-heal levels', () => {
    const label = (text: string) => `hatch-field__label">${text}<`;

    it('puts Priority and Pre-approval on one labelled row above Column', () => {
      const html = render(project({ canAdminister: true, autoHealBugEnabled: true }));

      const pair = html.indexOf('hatch-field-pair"');
      const column = html.indexOf(label('Column'), pair);
      expect(pair).toBeGreaterThan(-1);
      expect(column).toBeGreaterThan(pair);
      const row = html.slice(pair, column);
      expect(row).toContain(label('Priority'));
      expect(row).toContain(label('Pre-approval'));
      expect(row.indexOf(label('Priority'))).toBeLessThan(row.indexOf(label('Pre-approval')));
    });

    it('disables the whole fieldset, the pair included, with auto-filing off', () => {
      const html = render(project({ canAdminister: true, autoHealBugEnabled: false }));

      const start = html.indexOf('hatch-landing-choice" disabled');
      const column = html.indexOf(label('Column'), start);
      expect(start).toBeGreaterThan(-1);
      expect(column).toBeGreaterThan(start);
      expect(html.slice(start, column)).toContain('hatch-field-pair"');
      expect(html.slice(start, start + 60)).toContain('disabled=""');
    });
  });
});
