import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { ReactElement } from 'react';
import { DndContext } from '@dnd-kit/core';
import { MemoryRouter } from 'react-router-dom';
import { Column, PhoneColumn } from './BoardPage';
// BoardPage reaches clientLogger, which reads `window` as it loads; there is no jsdom here.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));

import type { LineageCard } from '../lib/lineage';
import type { ColumnNormal, IssueCard, Status } from '../types';

/* BoardPage itself cannot be rendered with data here - there is no DOM runner -
   so the page's two column components are, which is where each card's age is
   worked out and handed to the card. */

const IN_PROGRESS = 3;
const HOUR = 3600;

const status: Status = {
  id: IN_PROGRESS,
  name: 'In Progress',
  sortOrder: 2,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: true,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: [],
  color: '#6b7280',
};

const card = (key: string, over: Partial<IssueCard> = {}): LineageCard =>
  ({
    key,
    projectKey: 'AER',
    type: 'task',
    title: key,
    statusId: IN_PROGRESS,
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
    localModel: 'none',
    localModelOwn: 'none',
    localModelFrom: null,
    humanWork: 'agent',
    humanWorkOwn: 'inherit',
    humanWorkFrom: null,
    runnerTags: [],
    runnerTagsOwn: [],
    runnerTagsFrom: [],
    dangerousOwn: null,
    dangerous: null,
    dangerousFrom: null,
    omittedRunnersOwn: [],
    omittedRunners: [],
    express: false,
    stalledAt: null,
    stalledWhy: null,
    held: false,
    pullRequestUrl: null,
    onePr: false,
    onePrOwn: null,
    onePrFrom: null,
    createdBy: 'Britta Perry',
    projectPriority: 'normal',
    depth: 0,
    ...over,
  }) as LineageCard;

const at = '2026-11-22T20:20:00Z';
const aged = card('AER-1', { aged: true, enteredColumnAt: '2026-11-22T06:00:00Z' });
const fresh = card('AER-2', { enteredColumnAt: '2026-11-22T19:00:00Z' });
const normals: ColumnNormal[] = [{ statusId: IN_PROGRESS, seconds: 6 * HOUR }];

const render = (node: ReactElement) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <DndContext>{node}</DndContext>
    </MemoryRouter>,
  );

const column = (cards: LineageCard[], over: { normals?: ColumnNormal[]; at?: string } = {}) =>
  render(
    <Column
      status={status}
      narrow={false}
      cards={cards}
      hidden={0}
      filtering={false}
      dropping={false}
      lit={false}
      blocked={null}
      tint={null}
      found={null}
      onPeek={() => {}}
      projectsByKey={new Map()}
      showProjectMark={false}
      doneHorizonHours={null}
      normals={over.normals ?? normals}
      at={'at' in over ? over.at : at}
      rows={null}
      spots={new Map()}
      statuses={[status]}
      stories={new Map()}
      collapsedEpics={new Set()}
      onToggleEpic={() => {}}
    />,
  );

const phone = (cards: LineageCard[], over: { normals?: ColumnNormal[]; at?: string } = {}) =>
  render(
    <PhoneColumn
      status={status}
      cards={cards}
      hidden={0}
      filtering={false}
      found={null}
      dropping={false}
      blocked={null}
      wip={null}
      projectsByKey={new Map()}
      showProjectMark={false}
      doneHorizonHours={null}
      normals={over.normals ?? normals}
      at={'at' in over ? over.at : at}
      statuses={[status]}
      stories={new Map()}
      collapsedEpics={new Set()}
      onToggleEpic={() => {}}
    />,
  );

describe.each([
  ['Column', column],
  ['PhoneColumn', phone],
])('%s, with an aged card', (_name, draw) => {
  it('shows the chip on the card the server marked aged, in the column\'s own words', () => {
    const html = draw([aged]);

    expect(html).toContain('hatch-card-age');
    expect(html).toContain('14h 20m');
    expect(html).toContain('In In Progress for 14h 20m. Normal here is under 6h (p90, last 30 days).');
    expect(html).toMatch(/class="hatch-card[^"]* aged/);
  });

  it('shows nothing on a card that is not aged', () => {
    const html = draw([fresh]);

    expect(html).not.toContain('hatch-card-age');
    expect(html).not.toMatch(/class="hatch-card[^"]* aged/);
  });

  it('marks only the aged one of two', () => {
    const html = draw([aged, fresh]);

    expect(html.match(/hatch-card-age"/g)).toHaveLength(1);
  });

  it('shows nothing where the column has no normal', () => {
    expect(draw([aged], { normals: [] })).not.toContain('hatch-card-age');
    expect(draw([aged], { normals: [{ statusId: IN_PROGRESS, seconds: null }] })).not.toContain('hatch-card-age');
  });
});
