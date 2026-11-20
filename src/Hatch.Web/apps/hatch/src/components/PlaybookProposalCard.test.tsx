import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PlaybookProposalCard } from './PlaybookProposalCard';
import type { Playbook, PlaybookProposalChange, Status } from '../types';

// PromptDiff reads lib/clientLogger at import, which reads `window` at import,
// and there is no jsdom here - the same stub PlaybookRow.test.tsx uses.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const BEFORE: Playbook = {
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

function change(overrides: Partial<PlaybookProposalChange> = {}): PlaybookProposalChange {
  return {
    id: 1,
    playbookId: 1,
    before: BEFORE,
    after: BEFORE,
    changedFields: [],
    outOfDate: false,
    outOfDateReason: null,
    ...overrides,
  };
}

describe('PlaybookProposalCard', () => {
  it('draws every cell plain, no diff and no out-of-date badge when nothing changed', () => {
    const html = renderToStaticMarkup(<PlaybookProposalCard change={change()} statuses={STATUSES} />);
    expect(html).not.toContain('hatch-playbook-proposal-cell--changed');
    expect(html).not.toContain('Prescribed text');
    expect(html).not.toContain('out of date');
  });

  it('marks only the changed row, and draws the prompt diff, when model and prompt changed', () => {
    const after: Playbook = { ...BEFORE, model: 'opus', prompt: 'Do it differently.' };
    const html = renderToStaticMarkup(
      <PlaybookProposalCard change={change({ after, changedFields: ['model', 'prompt'] })} statuses={STATUSES} />,
    );
    const rows = html.match(/<tr>.*?<\/tr>/gs) ?? [];
    const modelRow = rows.find((r) => r.includes('>Model<'))!;
    expect(modelRow.match(/hatch-playbook-proposal-cell--changed/g)).toHaveLength(2);
    const otherRows = rows.filter((r) => !r.includes('>Model<') && !r.includes('<th></th>'));
    for (const row of otherRows) {
      expect(row).not.toContain('hatch-playbook-proposal-cell--changed');
    }
    expect(html).toContain('aria-label="Prescribed text"');
    expect(html).toContain('aria-label="The operator&#x27;s own wording"');
  });

  it('shows the local model before and after, none when a side has none', () => {
    const after: Playbook = { ...BEFORE, localModel: 'fake:1b' };
    const html = renderToStaticMarkup(
      <PlaybookProposalCard change={change({ after, changedFields: ['localModel'] })} statuses={STATUSES} />,
    );
    const rows = html.match(/<tr>.*?<\/tr>/gs) ?? [];
    const localRow = rows.find((r) => r.includes('>Local model<'))!;
    expect(localRow).toContain('none');
    expect(localRow).toContain('fake:1b');
    expect(localRow.match(/hatch-playbook-proposal-cell--changed/g)).toHaveLength(2);
  });

  it('shows the answers-questions flag before and after as no and yes', () => {
    const after: Playbook = { ...BEFORE, answersQuestions: true };
    const html = renderToStaticMarkup(
      <PlaybookProposalCard change={change({ after, changedFields: ['answersQuestions'] })} statuses={STATUSES} />,
    );
    const rows = html.match(/<tr>.*?<\/tr>/gs) ?? [];
    const flagRow = rows.find((r) => r.includes('>Answers questions<'))!;
    expect(flagRow).toContain('>no<');
    expect(flagRow).toContain('>yes<');
    expect(flagRow.match(/hatch-playbook-proposal-cell--changed/g)).toHaveLength(2);
  });

  it('shows the out-of-date badge and its exact reason', () => {
    const html = renderToStaticMarkup(
      <PlaybookProposalCard
        change={change({ outOfDate: true, outOfDateReason: 'edited since it was proposed' })}
        statuses={STATUSES}
      />,
    );
    expect(html).toContain('out of date');
    expect(html).toContain('edited since it was proposed');
  });

  it('names a review playbook by its one column, not an arrow', () => {
    const before: Playbook = { ...BEFORE, toStatusId: BEFORE.fromStatusId, toStatusName: BEFORE.fromStatusName };
    const html = renderToStaticMarkup(
      <PlaybookProposalCard change={change({ before, after: before })} statuses={STATUSES} />,
    );
    expect(html).toContain('review playbook');
    expect(html).not.toContain('hatch-playbook-arrow');
  });

  it('falls back to the plain status name when a status id is not in the list', () => {
    const before: Playbook = { ...BEFORE, fromStatusId: 99, fromStatusName: 'Gone' };
    const html = renderToStaticMarkup(
      <PlaybookProposalCard change={change({ before, after: before })} statuses={STATUSES} />,
    );
    expect(html).toContain('Gone');
  });
});
