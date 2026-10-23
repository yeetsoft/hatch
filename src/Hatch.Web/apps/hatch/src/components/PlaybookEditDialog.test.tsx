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
