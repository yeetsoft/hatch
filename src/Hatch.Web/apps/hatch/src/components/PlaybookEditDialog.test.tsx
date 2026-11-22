import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PlaybookEditDialog } from './PlaybookEditDialog';
import type { Playbook, Project, Status } from '../types';

// The Prompt field's MarkdownEditor pulls in lib/clientLogger, which reads
// `window` at import, and there is no jsdom here - the same stub
// NewIssueDialog.test.tsx uses. What is proven below is the markup around the
// field, never the field.
vi.mock('./MarkdownEditor', () => ({ MarkdownEditor: () => null }));
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const BASE: Playbook = {
  id: 1,
  fromStatusId: 1,
  fromStatusName: 'To Do',
  toStatusId: 2,
  toStatusName: 'In Progress',
  types: [],
  shape: 'any',
  prompt: 'Do the thing.',
  defaultPrompt: null,
  model: 'sonnet',
  effort: 'medium',
  budget: null,
  briefLimit: null,
  deniedTools: [],
  isCheck: false,
  answersQuestions: false,
  worksOutChanges: false,
  shapesAmbition: false,
  quietLimitSeconds: null,
  unreachableReason: null,
  maxRunSeconds: null,
  localModel: null,
  updatedAt: '2026-01-01T00:00:00Z',
  projectId: null,
  projectKey: null,
  projectName: null,
};

const STATUSES: Status[] = [
  { id: 1, name: 'To Do', sortOrder: 1, isTerminal: false, isDeferred: false, isWip: false, expressSkips: false, parentPulls: false, agentFiles: false, isImplementation: false, isProtected: false, mergesPullRequest: false, runnerTags: [], color: '#888888' },
  { id: 2, name: 'In Progress', sortOrder: 2, isTerminal: false, isDeferred: false, isWip: false, expressSkips: false, parentPulls: false, agentFiles: false, isImplementation: false, isProtected: false, mergesPullRequest: false, runnerTags: [], color: '#888888' },
];

const ONE_PROJECT: Project[] = [
  {
    id: 1,
    key: 'AER',
    name: 'Aerospace',
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
    noCodeCloses: false,
    noCodeClosesBy: null,
    noCodeClosesAt: null,
    runnersWorkContributions: false,
    runnersWorkContributionsBy: null,
    runnersWorkContributionsAt: null,
    filesContributions: false,
    filesContributionsBy: null,
    filesContributionsAt: null,
    allowsUnsignedCommits: true,
    allowsUnsignedCommitsBy: null,
    allowsUnsignedCommitsAt: null,
    onePullRequestPolicy: 'optional',
    onePullRequestPolicyBy: null,
    onePullRequestPolicyAt: null,
    priority: 'normal',
    priorityBy: null,
    priorityAt: null,
  },
];

const PRESCRIBED = 'aria-label="Prescribed text"';
const DROP = 'Drop the operator&#x27;s own wording';

function noop() {}

function render(playbook: Playbook | null) {
  return renderToStaticMarkup(
    <PlaybookEditDialog open playbook={playbook} statuses={STATUSES} projects={ONE_PROJECT} onClose={noop} onSaved={noop} />,
  );
}

/** The Save button's opening tag, to read `disabled` off. */
function saveButton(html: string): string {
  const end = html.indexOf('>Save<');
  return html.slice(html.lastIndexOf('<button', end), end);
}

/** A checkbox's opening tag, found by the text of the label that wraps it. */
function checkbox(html: string, label: string): string {
  const end = html.indexOf(`${label}</label>`);
  return html.slice(html.lastIndexOf('<input', end), end);
}

describe('PlaybookEditDialog flag boxes', () => {
  it('opens a flagged row with Answers questions ticked and the other two disabled', () => {
    const html = render({ ...BASE, answersQuestions: true });
    expect(checkbox(html, 'Answers questions')).toContain('checked');
    expect(checkbox(html, 'Answers questions')).not.toContain('disabled');
    expect(checkbox(html, 'Is check')).toContain('disabled');
    expect(checkbox(html, 'Is check')).not.toContain('checked');
    expect(checkbox(html, 'Works out changes')).toContain('disabled');
    expect(checkbox(html, 'Works out changes')).not.toContain('checked');
  });

  it('opens a check with the other two disabled and unticked', () => {
    const html = render({ ...BASE, isCheck: true });
    expect(checkbox(html, 'Is check')).toContain('checked');
    expect(checkbox(html, 'Is check')).not.toContain('disabled');
    expect(checkbox(html, 'Answers questions')).toContain('disabled');
    expect(checkbox(html, 'Answers questions')).not.toContain('checked');
    expect(checkbox(html, 'Works out changes')).toContain('disabled');
    expect(checkbox(html, 'Works out changes')).not.toContain('checked');
  });

  it('opens a works-out-changes row with the other two disabled and unticked', () => {
    const html = render({ ...BASE, worksOutChanges: true });
    expect(checkbox(html, 'Works out changes')).toContain('checked');
    expect(checkbox(html, 'Works out changes')).not.toContain('disabled');
    expect(checkbox(html, 'Is check')).toContain('disabled');
    expect(checkbox(html, 'Is check')).not.toContain('checked');
    expect(checkbox(html, 'Answers questions')).toContain('disabled');
    expect(checkbox(html, 'Answers questions')).not.toContain('checked');
  });

  it('opens a shapes-an-ambition row with the other three disabled and unticked', () => {
    const html = render({ ...BASE, shapesAmbition: true });
    expect(checkbox(html, 'Shapes an ambition')).toContain('checked');
    expect(checkbox(html, 'Shapes an ambition')).not.toContain('disabled');
    for (const label of ['Is check', 'Answers questions', 'Works out changes']) {
      expect(checkbox(html, label)).toContain('disabled');
      expect(checkbox(html, label)).not.toContain('checked');
    }
  });

  it('disables Shapes an ambition on a row that is any of the other three', () => {
    for (const flags of [{ isCheck: true }, { answersQuestions: true }, { worksOutChanges: true }]) {
      const html = render({ ...BASE, ...flags });
      expect(checkbox(html, 'Shapes an ambition')).toContain('disabled');
      expect(checkbox(html, 'Shapes an ambition')).not.toContain('checked');
    }
  });

  it('opens a plain row and a new playbook with all four boxes unticked and enabled', () => {
    for (const html of [render(BASE), render(null)]) {
      for (const label of ['Is check', 'Answers questions', 'Works out changes', 'Shapes an ambition']) {
        expect(checkbox(html, label)).not.toContain('checked');
        expect(checkbox(html, label)).not.toContain('disabled');
      }
    }
  });

  it('says what the Answers questions box is for', () => {
    expect(render(null)).toContain(
      'The row a session runs on to answer one question asked under an ambition. It edits nothing. A row is this, a check, a row that works out changes or a row that shapes an ambition - never more than one.',
    );
  });

  it('says what the Works out changes box is for', () => {
    expect(render(null)).toContain(
      'The row a session runs on to work out what a change to an ambition does to its epics in flight. It edits nothing. A row is this, a check, a row that answers questions or a row that shapes an ambition - never more than one.',
    );
  });

  it('says what the Shapes an ambition box is for', () => {
    expect(render(null)).toContain(
      'The row a session runs on to shape a draft ambition: its title, its description and its capabilities. It edits nothing in the tree. A row is this, a check, a row that answers questions or a row that works out changes - never more than one.',
    );
  });
});

describe('PlaybookEditDialog own-wording panel', () => {
  it('offers to drop a prompt that differs from the prescribed text', () => {
    expect(render({ ...BASE, defaultPrompt: 'Do it differently.' })).toContain(DROP);
  });

  it('draws nothing when the prompt is the prescribed text', () => {
    expect(render({ ...BASE, defaultPrompt: BASE.prompt })).not.toContain(DROP);
  });

  it('draws nothing for a row with no prescribed text', () => {
    expect(render({ ...BASE, defaultPrompt: null })).not.toContain(DROP);
  });

  it('draws nothing for a new playbook', () => {
    expect(render(null)).not.toContain(DROP);
  });

  it('lets Save take a blank prompt on a row with a prescribed text', () => {
    const html = render({ ...BASE, prompt: '', defaultPrompt: 'Do it differently.' });
    expect(saveButton(html)).not.toContain('disabled');
  });

  it('still refuses a blank prompt on a row with no prescribed text', () => {
    const html = render({ ...BASE, prompt: '', defaultPrompt: null });
    expect(saveButton(html)).toContain('disabled');
  });

  it('shows both texts in plain read-only boxes before the editor loads', () => {
    const html = render({ ...BASE, defaultPrompt: 'Do it differently.' });
    expect(html).toContain(PRESCRIBED);
    expect(html).toContain('Do it differently.');
    expect(html).toContain('readOnly');
  });

  it('shows no prescribed text for a row with no override', () => {
    expect(render({ ...BASE, defaultPrompt: BASE.prompt })).not.toContain(PRESCRIBED);
  });

  it('shows no prescribed text for a row with none', () => {
    expect(render({ ...BASE, defaultPrompt: null })).not.toContain(PRESCRIBED);
  });

  it('shows no prescribed text for a new playbook', () => {
    expect(render(null)).not.toContain(PRESCRIBED);
  });
});

describe('PlaybookEditDialog local model field', () => {
  it('opens a row with a local model holding its id', () => {
    const html = render({ ...BASE, localModel: 'fake:1b' });
    expect(html).toContain('Local model');
    expect(html).toContain('value="fake:1b"');
  });

  it('opens a row with none, and a new playbook, with the label and no id', () => {
    for (const html of [render(BASE), render(null)]) {
      expect(html).toContain('Local model');
      expect(html).not.toContain('fake:1b');
    }
  });

  it('says what blank means', () => {
    expect(render(BASE)).toContain('Leave blank and it never runs locally.');
  });
});

describe('PlaybookEditDialog quiet limit and run limit fields', () => {
  const QUIET_HINT = 'Seconds a claim may go without a word before the board lets go of it.';
  const RUN_HINT = 'Seconds a session may run before it is cut off. Leave blank for no limit.';

  it('opens a row with both limits holding their seconds', () => {
    const html = render({ ...BASE, quietLimitSeconds: 1500, maxRunSeconds: 3600 });
    expect(html).toContain('Quiet limit (seconds)');
    expect(html).toContain('Run limit (seconds)');
    expect(html).toContain('value="1500"');
    expect(html).toContain('value="3600"');
  });

  it('opens a row with null for both with the labels and no seconds', () => {
    const html = render(BASE);
    expect(html).toContain('Quiet limit (seconds)');
    expect(html).toContain('Run limit (seconds)');
    expect(html).not.toContain('value="1500"');
    expect(html).not.toContain('value="3600"');
  });

  it('says what each blank means', () => {
    const html = render(BASE);
    expect(html).toContain(QUIET_HINT);
    expect(html).toContain('Leave blank to use the board&#x27;s own quiet window.');
    expect(html).toContain(RUN_HINT);
  });

  it('opens a new playbook with both blank', () => {
    const html = render(null);
    expect(html).toContain('Quiet limit (seconds)');
    expect(html).toContain('Run limit (seconds)');
    expect(html).not.toContain('value="1500"');
    expect(html).not.toContain('value="3600"');
  });
});
