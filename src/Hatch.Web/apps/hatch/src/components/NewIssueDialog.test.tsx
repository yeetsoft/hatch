import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { NewIssueDialog } from './NewIssueDialog';
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

const noop = () => undefined;

describe('NewIssueDialog', () => {
  it('opens with no project chosen, and File it disabled', () => {
    const html = renderToStaticMarkup(
      dialog({ open: true, projects: [PROJECT], candidates: [], onClose: noop, onCreated: noop }),
    );

    expect(html).toContain('— choose a project —');
    const options = [...html.matchAll(/<option value="([^"]*)"/g)].map((m) => m[1]);
    expect(options.filter((value) => value === '')).toEqual(['']);

    const fileIt = /<button[^>]*>File it<\/button>/.exec(html)![0];
    expect(fileIt).toContain('disabled=""');
  });

  it('says so and offers no File it when there are no projects at all', () => {
    const html = renderToStaticMarkup(
      dialog({ open: true, projects: [], candidates: [], onClose: noop, onCreated: noop }),
    );

    expect(html).toContain('No projects yet. Make one on the Projects page first.');
    expect(html).not.toContain('File it');
  });
});
