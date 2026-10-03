import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { IssuePeek } from './IssuePeek';
import type { IssueCard, Status } from '../types';

// The Description field's MarkdownEditor reads `window.matchMedia` on render
// and there is no jsdom here (see vitest config, and lib/clientLogger.ts) -
// a real render of it throws before a single assertion runs. Nothing below
// touches Description, so a stub costs nothing these tests care about. Same
// fix as NewIssueDialog.test.tsx.
vi.mock('./MarkdownEditor', () => ({ MarkdownEditor: () => null }));

/* No DOM here, so this checks the two branches useStandalone() drives and not
   layout: that's the operator's browser. */
const noop = () => undefined;

afterEach(() => vi.unstubAllGlobals());

const status = (over: Partial<Status> = {}): Status => ({
  id: 1,
  name: 'To Do',
  sortOrder: 0,
  isTerminal: false,
  isDeferred: false,
  isWip: true,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  color: '#888888',
  ...over,
});

const card = (over: Partial<IssueCard> = {}): IssueCard => ({
  key: 'AER-1',
  projectKey: 'AER',
  type: 'task',
  title: 'A task',
  statusId: 1,
  rank: 0,
  parentKey: null,
  readyAt: null,
  dueAt: null,
  openQuestions: 0,
  assignee: null,
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  express: false,
  ...over,
});

const render = () =>
  renderToStaticMarkup(
    <MemoryRouter>
      <IssuePeek
        card={card()}
        status={status()}
        statuses={[status()]}
        directory={null}
        onExpedited={noop}
        onMove={() => Promise.resolve()}
        onClose={noop}
      />
    </MemoryRouter>,
  );

describe('IssuePeek', () => {
  it('shows "New tab" beside "Open the issue" when not standalone', () => {
    vi.stubGlobal('window', {
      matchMedia: () => ({ matches: false, addEventListener: noop, removeEventListener: noop }),
      navigator: {},
      location: { pathname: '/apps/hatch/' },
    });

    const html = render();

    expect(html).toContain('New tab');
    expect(html).toContain('Open the issue');
  });

  it('drops "New tab" and keeps "Open the issue" when standalone', () => {
    vi.stubGlobal('window', {
      matchMedia: () => ({ matches: true, addEventListener: noop, removeEventListener: noop }),
      navigator: {},
      location: { pathname: '/apps/hatch/' },
    });

    const html = render();

    expect(html).not.toContain('New tab');
    expect(html).toContain('Open the issue');
  });
});
