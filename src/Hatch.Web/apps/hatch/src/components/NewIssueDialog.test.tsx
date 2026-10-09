import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { NewIssueDialog } from './NewIssueDialog';
import { REMEMBERED_PROJECT_STORAGE_KEY } from '../lib/defaultProject';
import { IssueConfirmationsContext, type IssueConfirmations } from '../lib/useIssueConfirmations';
import type { Project } from '../types';

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
        onClose: noop,
        onCreated: noop,
      }),
    );

    expect(html).toContain('HATCH-…');
  });

  it('shows no key preview and no File it when there are no projects at all', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      dialog({ open: true, projects: [], candidates: [], defaultProjectKey: '', onClose: noop, onCreated: noop }),
    );

    expect(html).toContain('No projects yet. Make one on the Projects page first.');
    expect(html).not.toContain('File it');
    expect(html).not.toContain('hatch-key-preview');
  });
});
