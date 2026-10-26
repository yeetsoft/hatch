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

// Records the props the peek hands Modal, which is what its Escape listener
// and its X both call. The real Modal still renders, so the markup tests are
// unaffected. Hoisted because vi.mock is.
const recorded = vi.hoisted(() => ({ props: null as null | { onClose: () => void } }));
vi.mock('@hatch/ui', async (importOriginal) => {
  const ui = await importOriginal<typeof import('@hatch/ui')>();
  return {
    ...ui,
    Modal: (p: Parameters<typeof ui.Modal>[0]) => {
      recorded.props = p;
      return ui.Modal(p);
    },
  };
});

/* No DOM here, so this checks the two branches useStandalone() drives and not
   layout: that's docs/browser-testing.md's, not the operator's alone. */
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
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: [],
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
  preApproval: 'none',
  preApprovalOwn: 'none',
  preApprovalFrom: null,
  runnerTags: [],
  runnerTagsOwn: [],
  runnerTagsFrom: [],
  dangerousOwn: null,
  dangerous: null,
  dangerousFrom: null,
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
  ...over,
});

const render = (onClose: () => void = noop) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <IssuePeek
        card={card()}
        status={status()}
        statuses={[status()]}
        directory={null}
        onExpedited={noop}
        onMove={() => Promise.resolve()}
        onDelete={() => Promise.resolve()}
        onClose={onClose}
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

  it('draws a danger Delete button in the footer', () => {
    vi.stubGlobal('window', {
      matchMedia: () => ({ matches: false, addEventListener: noop, removeEventListener: noop }),
      navigator: {},
      location: { pathname: '/apps/hatch/' },
    });

    const html = render();

    expect(html).toContain('hatch-btn--danger');
    expect(html).toContain('>Delete<');
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

  it('drops the footer Close button but keeps the X', () => {
    vi.stubGlobal('window', {
      matchMedia: () => ({ matches: false, addEventListener: noop, removeEventListener: noop }),
      navigator: {},
      location: { pathname: '/apps/hatch/' },
    });

    const html = render();

    expect(html).not.toContain('>Close<');
    expect(html).toContain('aria-label="Close"');
    expect(html).toContain('>Delete<');
    expect(html).toContain('Open the issue');
  });

  it('hands Modal its own onClose, which Escape and the X both call', () => {
    vi.stubGlobal('window', {
      matchMedia: () => ({ matches: false, addEventListener: noop, removeEventListener: noop }),
      navigator: {},
      location: { pathname: '/apps/hatch/' },
    });
    const onClose = vi.fn();

    render(onClose);
    recorded.props?.onClose();

    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
