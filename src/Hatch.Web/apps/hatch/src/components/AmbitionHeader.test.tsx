import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { AmbitionCrumb, AmbitionHeader } from './AmbitionHeader';
import { MeContext } from '../lib/useMe';
import type { AmbitionRow, Issue, Me } from '../types';

const issue = (over: Partial<Issue> = {}): Issue =>
  ({
    key: 'AER-1',
    projectKey: 'AER',
    type: 'ambition',
    title: 'Tickets in a human voice',
    wipLimit: null,
    preApproval: 'none',
    ...over,
  }) as Issue;

const row = (over: Partial<AmbitionRow> = {}): AmbitionRow => ({
  key: 'AER-1',
  projectKey: 'AER',
  title: 'ambition',
  statusId: 1,
  statusName: 'Doing',
  stage: 'running',
  stageWhy: '1 of its epics is open, and it runs 1 at once.',
  limit: 1,
  capabilities: 4,
  holds: 1,
  inFlight: 1,
  waitsOnPerson: 1,
  notStarted: 1,
  newlyHolding: 0,
  open: [],
  summary: '',
  decisions: 0,
  budget: { budgetUsd: null, spentUsd: 0, unpricedSessions: 0, spent: false },
  childBeyondScope: false,
  shapingStatusId: null,
  shapingStatusName: null,
  startStatusId: null,
  startStatusName: null,
  ...over,
});

const meOf = (over: Partial<Me> = {}): Me => ({
  kind: 'person',
  name: '',
  role: 'user',
  configured: true,
  canSignOut: true,
  canSignIn: false,
  ...over,
});

const renderHeader = (over: Partial<React.ComponentProps<typeof AmbitionHeader>> = {}, me: Me = meOf()): string =>
  renderToStaticMarkup(
    <MeContext.Provider value={{ me, isAdmin: false, settled: true }}>
      <MemoryRouter>
        <AmbitionHeader
          issue={issue()}
          row={row()}
          rowError={null}
          project={null}
          passCount={3}
          onSetLimit={() => {}}
          onMoved={() => {}}
          {...over}
        />
      </MemoryRouter>
    </MeContext.Provider>,
  );

describe('AmbitionCrumb', () => {
  it('reads "Ambitions / the title" with no scope', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AmbitionCrumb title="Tickets in a human voice" scopeKeys={[]} />
      </MemoryRouter>,
    );

    expect(html).toContain('Ambitions');
    expect(html).toContain('/ Tickets in a human voice');
    expect(html).toContain('href="/ambitions"');
  });

  it('links to the Ambitions page under the tab\'s scope', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AmbitionCrumb title="Tickets in a human voice" scopeKeys={['AER', 'HA']} />
      </MemoryRouter>,
    );

    expect(html).toContain('href="/ambitions?scope=AER,HA"');
  });
});

describe('AmbitionHeader', () => {
  it('draws the stage word and its sentence', () => {
    const html = renderHeader({ row: row({ stage: 'budget spent', stageWhy: 'Its budget is spent.' }) });

    expect(html).toContain('Budget spent');
    expect(html).toContain('Its budget is spent.');
  });

  it('draws the bar\'s aria-label from the four counts', () => {
    const html = renderHeader({ row: row({ holds: 2, inFlight: 1, waitsOnPerson: 0, notStarted: 3 }) });

    expect(html).toContain('aria-label="2 hold, 1 in flight, 0 waiting on a person, 3 not started"');
  });

  it('draws the four facts: project, epics at once, passes, pre-approval', () => {
    const html = renderHeader({
      issue: issue({ projectKey: 'AER', wipLimit: 2, preApproval: 'preapproved' }),
      passCount: 6,
    });

    expect(html).toContain('AER');
    expect(html).toContain('Epics at once');
    expect(html).toContain('6 passes so far');
    expect(html).toContain('Pre-approved');
  });

  it('reads one pass as singular', () => {
    const html = renderHeader({ passCount: 1 });

    expect(html).toContain('1 pass so far');
  });

  it('omits the passes fact while unread', () => {
    const html = renderHeader({ passCount: null });

    expect(html).not.toContain('passes so far');
    expect(html).not.toContain('pass so far');
  });

  it('draws one danger line in place of the pill, the sentence and the bar when the row failed, and still draws the facts', () => {
    const html = renderHeader({ row: null, rowError: 'could not read the Ambitions row', issue: issue({ wipLimit: 3 }) });

    expect(html).toContain('could not read the Ambitions row');
    expect(html).not.toContain('hatch-stage-pill');
    expect(html).toContain('Epics at once');
  });

  it('draws nothing where capabilities are zero, in place of the bar', () => {
    const html = renderHeader({ row: row({ capabilities: 0, holds: 0, inFlight: 0, waitsOnPerson: 0, notStarted: 0 }) });

    expect(html).toContain('This ambition has no capabilities yet.');
  });

  it('offers Shape it for a draft whose project has a shaping row', () => {
    const html = renderHeader({ row: row({ stage: 'draft', shapingStatusId: 2, shapingStatusName: 'Idea' }) });

    expect(html).toContain('Shape it');
    expect(html).not.toContain('Start it');
  });

  it('offers Start it for a ready row', () => {
    const html = renderHeader({ row: row({ stage: 'ready', startStatusId: 3, startStatusName: 'Doing' }) });

    expect(html).toContain('Start it');
    expect(html).not.toContain('Shape it');
  });

  it('offers neither press for a running row', () => {
    const html = renderHeader({ row: row({ stage: 'running' }) });

    expect(html).not.toContain('Shape it');
    expect(html).not.toContain('Start it');
  });

  it('offers neither press to a key', () => {
    const html = renderHeader({ row: row({ stage: 'draft', shapingStatusId: 2 }) }, meOf({ kind: 'key' }));

    expect(html).not.toContain('Shape it');
  });
});
