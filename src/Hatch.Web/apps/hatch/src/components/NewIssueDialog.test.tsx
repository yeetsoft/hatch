import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { NewIssueDialog, omitDefault } from './NewIssueDialog';
import { REMEMBERED_PROJECT_STORAGE_KEY } from '../lib/defaultProject';
import { IssueConfirmationsContext, type IssueConfirmations } from '../lib/useIssueConfirmations';
import type { AssigneeDirectory, Project } from '../types';

const directory = (kind: 'person' | 'key' = 'person'): AssigneeDirectory => ({
  me: { kind, id: 'ada', name: 'Ada' },
  assignees: [],
});

// The Description field's MarkdownEditor reads `window.matchMedia` on render
// and there is no jsdom here (see vitest config, and lib/clientLogger.ts) -
// a real render of it throws before a single assertion runs. Nothing below
// touches Description, so a stub costs nothing these tests care about.
vi.mock('./MarkdownEditor', () => ({ MarkdownEditor: () => null }));

// NewIssueDialog confirms a filing through this context - a renderless stub,
// never exercised by the opening-render assertions below.
const CONFIRMATIONS: IssueConfirmations = {
  confirm: () => undefined,
  moved: () => undefined,
  cascaded: () => undefined,
  undo: () => undefined,
  undoNewest: () => false,
  onUndone: () => () => undefined,
};

function dialog(props: Parameters<typeof NewIssueDialog>[0]) {
  return (
    <IssueConfirmationsContext.Provider value={CONFIRMATIONS}>
      <NewIssueDialog {...props} />
    </IssueConfirmationsContext.Provider>
  );
}

// Same stub phoneBoard.test.ts uses for localStorage, there being no jsdom here.
function stubStorage(initial: Record<string, string> = {}) {
  const data = { ...initial };
  const storage = {
    getItem: (k: string) => (k in data ? data[k] : null),
    setItem: (k: string, v: string) => {
      data[k] = v;
    },
  };
  vi.stubGlobal('window', { localStorage: storage });
  return data;
}

afterEach(() => vi.unstubAllGlobals());

const PROJECT: Project = {
  id: 1,
  key: 'HATCH',
  name: 'Hatch',
  issueCount: 0,
  createdAt: '2024-01-01T00:00:00Z',
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
  reflectionCadenceDays: null,
  reflectionLastPeriodStart: null,
  reflectionNextDueAt: null,
};

const OTHER: Project = {
  id: 2,
  key: 'AER',
  name: 'Aerie',
  issueCount: 0,
  createdAt: '2024-01-01T00:00:00Z',
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
  reflectionCadenceDays: null,
  reflectionLastPeriodStart: null,
  reflectionNextDueAt: null,
};

const noop = () => undefined;

describe('NewIssueDialog', () => {
  it('defaults to the project named by defaultProjectKey', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      dialog({
        open: true,
        projects: [PROJECT, OTHER],
        candidates: [],
        defaultProjectKey: 'AER',
        directory: null,
        onClose: noop,
        onCreated: noop,
      }),
    );

    expect(html).toContain('AER-…');
    const fileIt = /<button[^>]*>File it<\/button>/.exec(html)![0];
    expect(fileIt).not.toContain('disabled=""');
  });

  it('falls back to the remembered project when the filter names none', () => {
    stubStorage({ [REMEMBERED_PROJECT_STORAGE_KEY]: 'AER' });

    const html = renderToStaticMarkup(
      dialog({
        open: true,
        projects: [PROJECT, OTHER],
        candidates: [],
        defaultProjectKey: '',
        directory: null,
        onClose: noop,
        onCreated: noop,
      }),
    );

    expect(html).toContain('AER-…');
  });

  it('falls back to the first project with neither a filter nor a memory', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      dialog({
        open: true,
        projects: [PROJECT, OTHER],
        candidates: [],
        defaultProjectKey: '',
        directory: null,
        onClose: noop,
        onCreated: noop,
      }),
    );

    expect(html).toContain('HATCH-…');
  });

  it('offers no one-PR steps while Type is the default task', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      dialog({
        open: true,
        projects: [PROJECT],
        candidates: [],
        defaultProjectKey: '',
        directory: null,
        onClose: noop,
        onCreated: noop,
      }),
    );

    expect(html).not.toContain('hatch-one-pr-steps');
    expect(html).not.toContain('One pull request');
  });

  it('shows no key preview and no File it when there are no projects at all', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      dialog({ open: true, projects: [], candidates: [], defaultProjectKey: '', directory: null, onClose: noop, onCreated: noop }),
    );

    expect(html).toContain('No projects yet. Make one on the Projects page first.');
    expect(html).not.toContain('File it');
    expect(html).not.toContain('hatch-key-preview');
  });

  it('draws Priority and Pre-approval as pressable pills for a person', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      dialog({
        open: true,
        projects: [PROJECT],
        candidates: [],
        defaultProjectKey: '',
        directory: directory(),
        onClose: noop,
        onCreated: noop,
      }),
    );

    expect(html).toContain('Priority');
    expect(html).toContain('Pre-approval');
    expect(html).toContain('hatch-priority-pill-press');
    expect(html).toContain('hatch-preapproval-pill-press');
  });

  it('draws Priority and Pre-approval as words, with no pressable pill, for a key', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      dialog({
        open: true,
        projects: [PROJECT],
        candidates: [],
        defaultProjectKey: '',
        directory: directory('key'),
        onClose: noop,
        onCreated: noop,
      }),
    );

    expect(html).toContain('hatch-priority-said');
    expect(html).toContain('hatch-preapproval-said');
    expect(html).not.toContain('hatch-priority-pill-press');
    expect(html).not.toContain('hatch-preapproval-pill-press');
  });
});

describe('omitDefault', () => {
  it('leaves out a level left at its default', () => {
    expect(omitDefault('normal', 'normal')).toBeUndefined();
    expect(omitDefault('none', 'none')).toBeUndefined();
  });

  it('passes through a level that differs from its default', () => {
    expect(omitDefault('emergency', 'normal')).toBe('emergency');
    expect(omitDefault('preapproved', 'none')).toBe('preapproved');
  });
});
