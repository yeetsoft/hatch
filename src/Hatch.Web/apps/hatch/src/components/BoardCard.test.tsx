import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { ReactElement } from 'react';
import { DndContext } from '@dnd-kit/core';
import { SortableContext } from '@dnd-kit/sortable';
import { MemoryRouter } from 'react-router-dom';
import { BoardCard, CardPreview, lineClick } from './BoardCard';
import { TRAILS, STREAMING } from '../lib/tileMarks';
import { countWords, needsMe, stripLabel } from '../lib/storiesOnEpics';
import type { ContributionRecord, IssueCard, Project, Status } from '../types';

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
  ...over,
});

const project = (over: Partial<Project> = {}): Project => ({
  id: 1,
  key: 'AER',
  name: 'Aerial',
  issueCount: 1,
  createdAt: '2026-01-01T00:00:00Z',
  color: '#336699',
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
  ...over,
});

const status = (id: number, sortOrder: number, opts: Partial<Status> = {}): Status => ({
  id,
  name: `status-${id}`,
  sortOrder,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: [],
  color: '#6b7280',
  ...opts,
});

const between = (html: string, open: string) => {
  const start = html.indexOf(open);
  expect(start).toBeGreaterThanOrEqual(0);
  const end = html.indexOf('</div>', start);
  return html.slice(start, end);
};

// useSortable needs a DndContext above it and the id in a SortableContext's
// own items to resolve without throwing - neither does any dragging here, so
// a static render never exercises sensors or a DragOverlay.
const renderSortable = (node: ReactElement, ids: string[]) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <DndContext>
        <SortableContext items={ids}>{node}</SortableContext>
      </DndContext>
    </MemoryRouter>,
  );

describe('BoardCard CardFace', () => {
  it('draws only the project mark and the key on the head, no type word and no enhancers', () => {
    const full = card({
      priority: 'expedited',
      express: true,
      dangerous: 'x',
      claim: { claimedBy: 'hatch', runner: 'Jeff Winger', claimedAt: new Date().toISOString(), heartbeatAt: new Date().toISOString(), chatter: null, chatterAt: null, ttlSeconds: 300, stallLapseSeconds: 600 },
      openQuestions: 2,
      held: true,
      assignee: { kind: 'person', id: '1', name: 'Ada' },
    });
    const html = renderToStaticMarkup(
      <CardPreview card={full} waiting={false} terminal={false} project={project()} />,
    );

    const head = between(html, '<div class="hatch-card-head">');
    expect(head).toContain('hatch-project-mark');
    expect(head).toContain('hatch-card-key');
    expect(head).not.toContain('hatch-card-expedited');
    expect(head).not.toContain('hatch-card-dangerous');
    expect(head).not.toContain('hatch-card-preapproved');
    expect(head).not.toContain('hatch-card-claim');
    expect(head).not.toContain('hatch-card-asking');
    expect(head).not.toContain('hatch-card-held');
    expect(head).not.toContain('hatch-card-assignee');
    expect(head).not.toContain('hatch-badge');

    // The type is a glyph on the card now, not a word anywhere on it - fixture's
    // default type is 'task'.
    expect(html).toContain('hatch-type-glyph--task');
    expect(html).not.toContain('>epic<');
    expect(html).not.toContain('>story<');
    expect(html).not.toContain('>task<');
    expect(html).not.toContain('>bug<');
  });

  const face = (over: Partial<IssueCard>) =>
    renderToStaticMarkup(<CardPreview card={card(over)} waiting={false} terminal={false} project={project()} />);
  const glyphOf = (html: string) => {
    const start = html.indexOf('<span class="hatch-type-glyph');
    expect(start).toBeGreaterThanOrEqual(0);
    return html.slice(start, html.indexOf('</span>', start));
  };
  const trailLines = (html: string) => (glyphOf(html).match(/hatch-type-tile-trail/g) ?? []).length;

  it.each([
    ['emergency', 'task; Emergency - the highest priority, first above everything expedited'],
    ['expedited', 'task; Expedited - this one goes first'],
    ['economy', 'task; Economy - worked only with usage that would otherwise go spare'],
  ] as const)('titles the tile with its type, then the %s sentence', (priority, title) => {
    expect(face({ priority })).toContain(`title="${title}" aria-label="${title}"`);
  });

  it('titles the tile with the express and the pre-approved sentences, one at a time', () => {
    const express = 'Express - carried past a column marked Express skips, with no session';
    const preapproved =
      'Pre-approved - the approve press given in advance: carried into the merge column, or closed, with no session';
    expect(face({ preApproval: 'express', express: true })).toContain(`title="task; ${express}"`);
    // Pre-approved carries the express flag too, and says its own sentence alone.
    const both = face({ preApproval: 'preapproved', express: true });
    expect(both).toContain(`title="task; ${preapproved}"`);
    expect(both).not.toContain('Express - carried');
    expect(face({ priority: 'emergency', preApproval: 'preapproved', express: true })).toContain(
      `title="task; Emergency - the highest priority, first above everything expedited; ${preapproved}"`,
    );
  });

  it('titles a bare tile with the type word alone, whatever low and paused say on the row', () => {
    for (const over of [{}, { priority: 'low' as const }, { priority: 'paused' as const }]) {
      expect(face(over)).toContain('title="task" aria-label="task"');
    }
  });

  it('draws the tile as the flame and the trail say, by level', () => {
    expect(glyphOf(face({ priority: 'emergency' }))).toContain('fill:var(--heat)');
    expect(glyphOf(face({ priority: 'expedited' }))).toContain('fill:var(--heat)');
    expect(glyphOf(face({ priority: 'economy' }))).not.toContain('fill:var(--heat)');
    expect(glyphOf(face({ priority: 'economy' }))).toContain('opacity="0.4"');
    expect(glyphOf(face({}))).not.toContain('fill:var(--heat)');
    // The trail reads the level, never the express flag.
    expect(trailLines(face({ preApproval: 'none', express: true }))).toBe(0);
    expect(glyphOf(face({ preApproval: 'express' }))).toContain(`d="${TRAILS.express.lines}"`);
    expect(glyphOf(face({ preApproval: 'preapproved' }))).toContain(`d="${TRAILS.preapproved.lines}"`);
    expect(glyphOf(face({ preApproval: 'preapproved' }))).toContain('hatch-type-tile-lean');
    expect(glyphOf(face({ priority: 'emergency', preApproval: 'preapproved' }))).toContain(
      `d="${STREAMING.lines}"`,
    );
  });

  it('draws the same tile on the board card as on the drag preview', () => {
    const dragged = card({ priority: 'emergency', preApproval: 'preapproved' });
    const sortable = renderSortable(<BoardCard card={dragged} />, [dragged.key]);
    expect(glyphOf(sortable)).toBe(glyphOf(face({ priority: 'emergency', preApproval: 'preapproved' })));
  });

  it('no longer draws the emergency, expedited, economy, express or pre-approved marks', () => {
    for (const over of [
      { priority: 'emergency' as const },
      { priority: 'expedited' as const },
      { priority: 'economy' as const },
      { preApproval: 'express' as const, express: true },
      { preApproval: 'preapproved' as const, express: true },
    ]) {
      const html = face(over);
      for (const glyph of ['🚨', '↑', '🌙', '»', '✓']) expect(html, glyph).not.toContain(glyph);
      expect(html).not.toContain('hatch-card-marks');
      expect(html).not.toContain('hatch-card-preapproved');
      expect(html).not.toContain('hatch-card-expedited');
    }
  });

  it('keeps ↓ for low and ⏸ for paused, and on a normal card in a paused project', () => {
    expect(between(face({ priority: 'low' }), '<div class="hatch-card-marks">')).toContain('↓');
    expect(between(face({ priority: 'paused' }), '<div class="hatch-card-marks">')).toContain('⏸');
    expect(between(face({ projectPriority: 'paused' }), '<div class="hatch-card-marks">')).toContain('⏸');
  });

  it('keeps the other marks beside a tile that draws its level', () => {
    const html = face({ priority: 'emergency', preApproval: 'preapproved', express: true, dangerous: 'x' });
    const marks = between(html, '<div class="hatch-card-marks">');
    expect(marks).toContain('hatch-card-dangerous');
    expect(marks).not.toContain('hatch-card-expedited');
  });

  it('makes room for the trail: the contribution mark steps left and the head takes its padding', () => {
    const contribution: ContributionRecord = {
      pullRequestUrl: null,
      headBranch: null,
      authorLogin: 'someone',
      authorAssociation: 'NONE',
      filedAt: '2026-11-11T00:00:00Z',
      filedBy: 'runner',
    };
    const trailed = face({ preApproval: 'preapproved', contribution });
    expect(trailed).toContain('hatch-contribution-mark--card hatch-contribution-mark--trailed');
    expect(trailed).toContain('hatch-card-head hatch-card-head--trailed hatch-card-head--contribution');
    const plain = face({ contribution });
    expect(plain).not.toContain('hatch-contribution-mark--trailed');
    expect(plain).toContain('hatch-card-head hatch-card-head--contribution');
    expect(face({ preApproval: 'preapproved' })).toContain('hatch-card-head hatch-card-head--trailed"');
    expect(face({ priority: 'expedited' })).toContain('<div class="hatch-card-head">');
  });

  it('draws a muted passed-over mark for a level that was not honoured, titled with the sentence', () => {
    const html = renderToStaticMarkup(
      <CardPreview
        card={card({
          preApprovalPassedOver: 'OPS-1',
          preApprovalPassedOverLevel: 'preapproved',
          priorityPassedOver: 'OPS-1',
          priorityPassedOverLevel: 'expedited',
        })}
        waiting={false}
        terminal={false}
        project={project()}
      />,
    );

    const marks = between(html, '<div class="hatch-card-marks">');
    expect(marks).toContain('class="hatch-card-preapproved passedover" title="pre-approved on OPS-1, not honoured in AER"');
    expect(marks).toContain('class="hatch-card-expedited passedover" title="expedited on OPS-1, not honoured in AER"');
  });

  it('says outside your scope on a passed-over mark whose ancestor is masked', () => {
    const html = renderToStaticMarkup(
      <CardPreview
        card={card({ priorityPassedOverLevel: 'expedited', fromBeyondScope: ['priorityPassedOver'] })}
        waiting={false}
        terminal={false}
        project={project()}
      />,
    );

    expect(html).toContain('title="expedited from an issue outside your scope, not honoured in AER"');
  });

  it('titles an honoured mark with where it came from, else its fixed text', () => {
    const inherited = renderToStaticMarkup(
      <CardPreview
        card={card({ priority: 'expedited', priorityFrom: 'AER-0' })}
        waiting={false}
        terminal={false}
        project={project()}
      />,
    );
    expect(inherited).toContain('title="task; expedited on AER-0"');

    const own = renderToStaticMarkup(
      <CardPreview card={card({ priority: 'expedited' })} waiting={false} terminal={false} project={project()} />,
    );
    expect(own).toContain('title="task; Expedited - this one goes first"');
  });

  it('titles the parent row with the pull sentence', () => {
    const html = renderToStaticMarkup(
      <CardPreview
        card={card({ parentKey: 'OPS-1', pullPassedOver: 'OPS-1' })}
        waiting={false}
        terminal={false}
        project={null}
      />,
    );

    expect(html).toContain('title="OPS-1 is in progress, but its pull is not honoured in AER"');
  });

  it('wears the local-model mark at prefer and require, naming the ancestor, and none at none', () => {
    const draw = (over: Partial<IssueCard>) =>
      between(
        renderToStaticMarkup(
          <CardPreview card={card(over)} waiting={false} terminal={false} project={project()} />,
        ),
        '<div class="hatch-card-marks">',
      );

    const prefer = draw({ localModel: 'prefer', localModelFrom: 'HA-5' });
    expect(prefer).toContain('hatch-card-localmodel prefer');
    expect(prefer).toContain('title="Local model: prefer, inherited from HA-5"');
    const require = draw({ localModel: 'require', localModelOwn: 'require' });
    expect(require).toContain('hatch-card-localmodel require');
    expect(require).toContain('title="Local model: require"');
    expect(draw({ priority: 'low' })).not.toContain('hatch-card-localmodel');
  });

  it('wears the human-work mark when human, naming the ancestor, and not when agent', () => {
    const draw = (over: Partial<IssueCard>) =>
      between(
        renderToStaticMarkup(
          <CardPreview card={card(over)} waiting={false} terminal={false} project={project()} />,
        ),
        '<div class="hatch-card-marks">',
      );

    const inherited = draw({ humanWork: 'human', humanWorkFrom: 'HA-5' });
    expect(inherited).toContain('hatch-card-humanwork');
    expect(inherited).toContain(
      'title="Human work - a person does this one, and no runner picks it up, inherited from HA-5"',
    );
    const own = draw({ humanWork: 'human', humanWorkOwn: 'human' });
    expect(own).toContain('hatch-card-humanwork');
    expect(own).toContain('title="Human work - a person does this one, and no runner picks it up"');
    expect(
      renderToStaticMarkup(
        <CardPreview card={card({ humanWork: 'agent' })} waiting={false} terminal={false} project={project()} />,
      ),
    ).not.toContain('hatch-card-humanwork');
  });

  it('wears the dangerous mark, with the reason in its title, when dangerous', () => {
    const dangerous = card({ dangerous: 'load-tests the API' });
    const html = renderToStaticMarkup(
      <CardPreview card={dangerous} waiting={false} terminal={false} project={project()} />,
    );

    const marks = between(html, '<div class="hatch-card-marks">');
    expect(marks).toContain('hatch-card-dangerous');
    expect(marks).toContain('title="Dangerous - load-tests the API"');
  });

  it('wears the no-code mark, with the reason in its title, when finished with no code', () => {
    const noCode = card({ noCode: 'the setting was already right' });
    const html = renderToStaticMarkup(
      <CardPreview card={noCode} waiting={false} terminal={false} project={project()} />,
    );

    const marks = between(html, '<div class="hatch-card-marks">');
    expect(marks).toContain('hatch-card-nocode');
    expect(marks).toContain('title="Finished with no code - the setting was already right"');
  });

  it('wears no no-code mark on a card that was not declared finished with no code', () => {
    const bare = card();
    const html = renderToStaticMarkup(
      <CardPreview card={bare} waiting={false} terminal={false} project={project()} />,
    );

    expect(html).not.toContain('hatch-card-nocode');
  });

  it('wears the no-code mark alone on the row when the card is also pre-approved', () => {
    const both = card({ preApproval: 'preapproved', express: true, noCode: 'the setting was already right' });
    const html = renderToStaticMarkup(
      <CardPreview card={both} waiting={false} terminal={false} project={project()} />,
    );

    const marks = between(html, '<div class="hatch-card-marks">');
    expect(marks).not.toContain('hatch-card-preapproved');
    expect(marks).toContain('hatch-card-nocode');
  });

  it('wears the pause mark for its paused project, with the project-paused title, even at normal priority', () => {
    const inAPausedProject = card({ priority: 'normal', projectPriority: 'paused' });
    const html = renderToStaticMarkup(
      <CardPreview card={inAPausedProject} waiting={false} terminal={false} project={project()} />,
    );

    const marks = between(html, '<div class="hatch-card-marks">');
    expect(marks).toContain('hatch-card-expedited paused');
    expect(marks).toContain(
      'title="Paused - its project is paused; a person set it aside and the loop leaves it where it stands"',
    );
  });

  it('draws the marks row for a card with nothing set but a paused project', () => {
    const inAPausedProject = card({ priority: 'normal', projectPriority: 'paused' });
    const html = renderToStaticMarkup(
      <CardPreview card={inAPausedProject} waiting={false} terminal={false} project={null} />,
    );

    expect(html).toContain('hatch-card-marks');
  });

  it('draws every enhancer together on the marks row, in order, ending in the assignee', () => {
    const full = card({
      priority: 'low',
      express: true,
      dangerous: 'load-tests the API',
      preApproval: 'preapproved',
      claim: { claimedBy: 'hatch', runner: 'Jeff Winger', claimedAt: new Date().toISOString(), heartbeatAt: new Date().toISOString(), chatter: null, chatterAt: null, ttlSeconds: 300, stallLapseSeconds: 600 },
      openQuestions: 2,
      held: true,
      assignee: { kind: 'person', id: '1', name: 'Ada' },
    });
    const html = renderToStaticMarkup(
      <CardPreview card={full} waiting={false} terminal={false} project={project()} />,
    );

    const marks = between(html, '<div class="hatch-card-marks">');
    const classes = [
      'hatch-card-expedited',
      'hatch-card-dangerous',
      'hatch-card-claim',
      'hatch-card-asking',
      'hatch-card-held',
      'hatch-card-assignee',
    ];
    const indices = classes.map((c) => marks.indexOf(c));
    for (const i of indices) expect(i).toBeGreaterThanOrEqual(0);
    for (let i = 1; i < indices.length; i++) expect(indices[i]).toBeGreaterThan(indices[i - 1]);
    expect(indices[indices.length - 1]).toBe(marks.lastIndexOf('hatch-card-assignee'));
  });

  it('draws no marks row at all on a card with nothing to say', () => {
    const bare = card();
    const html = renderToStaticMarkup(
      <CardPreview card={bare} waiting={false} terminal={false} project={null} />,
    );

    expect(html).not.toContain('hatch-card-marks');
  });

  it('draws the parent chip from the card\'s own parentKey, with no parent card to look up', () => {
    const child = card({ parentKey: 'AER-999' });
    const html = renderToStaticMarkup(
      <CardPreview card={child} waiting={false} terminal={false} project={null} />,
    );

    // Asserted apart: the arrow and the key may be separate text nodes.
    expect(html).toContain('hatch-card-parent');
    expect(html).toContain('AER-999');
  });

  it('shows held and not stalled when both are set', () => {
    const both = card({ held: true, stalledAt: new Date().toISOString() });
    const html = renderToStaticMarkup(
      <CardPreview card={both} waiting={false} terminal={false} project={null} />,
    );

    expect(html).toContain('hatch-card-held');
    expect(html).not.toContain('hatch-card-stalled');
  });

  it('draws a dot per story, each coloured and titled from its own column, and the right "N of M done"', () => {
    const epic = card({ type: 'epic', key: 'AER-1' });
    const statuses = [
      status(1, 0, { color: '#111111' }),
      status(2, 1, { color: '#222222' }),
      status(3, 2, { color: '#333333', isTerminal: true }),
    ];
    const stories = [
      card({ key: 'AER-2', type: 'story', parentKey: 'AER-1', statusId: 1 }),
      card({ key: 'AER-3', type: 'story', parentKey: 'AER-1', statusId: 2 }),
      card({ key: 'AER-4', type: 'story', parentKey: 'AER-1', statusId: 3 }),
    ];
    const html = renderToStaticMarkup(
      <CardPreview card={epic} waiting={false} terminal={false} project={null} stories={stories} statuses={statuses} />,
    );

    expect(html.split('hatch-card-story-dot').length - 1).toBe(3);
    for (const [story, st] of [
      [stories[0], statuses[0]],
      [stories[1], statuses[1]],
      [stories[2], statuses[2]],
    ] as const) {
      const dotStart = html.indexOf(`title="${story.key} - ${st.name}"`);
      expect(dotStart).toBeGreaterThanOrEqual(0);
    }
    expect(html).toContain(countWords(3, 1));

    const strip = between(html, '<div class="hatch-card-stories"');
    expect(strip).toContain('aria-label="3 stories, 1 done"');
  });

  it('draws no stories strip when stories is absent or empty', () => {
    const bare = card();
    const noneHtml = renderToStaticMarkup(
      <CardPreview card={bare} waiting={false} terminal={false} project={null} />,
    );
    expect(noneHtml).not.toContain('hatch-card-stories');

    const emptyHtml = renderToStaticMarkup(
      <CardPreview card={bare} waiting={false} terminal={false} project={null} stories={[]} statuses={[]} />,
    );
    expect(emptyHtml).not.toContain('hatch-card-stories');
  });
});

// Every anchor's own span, start index inclusive and end index exclusive of
// its own `</a>` - used to check nothing is nested that should not be.
const anchorSpans = (html: string): Array<[number, number]> => {
  const spans: Array<[number, number]> = [];
  let from = 0;
  for (;;) {
    const start = html.indexOf('<a ', from);
    if (start < 0) break;
    const end = html.indexOf('</a>', start);
    expect(end).toBeGreaterThan(start);
    spans.push([start, end]);
    from = end + 4;
  }
  return spans;
};

describe('BoardCard CardFace, a contribution', () => {
  const record: ContributionRecord = {
    pullRequestUrl: null,
    headBranch: null,
    authorLogin: 'someone',
    authorAssociation: 'NONE',
    filedAt: '2026-11-11T00:00:00Z',
    filedBy: 'runner',
    originKey: 'HA-9',
    originInstance: 'https://hatch.example.org',
  };
  const face = (c: IssueCard) =>
    renderToStaticMarkup(<CardPreview card={c} waiting={false} terminal={false} project={project()} />);

  it('wears nothing new without a record', () => {
    const html = face(card());
    expect(html).not.toContain('hatch-contribution');
  });

  it('wears the mark beside the type glyph, on every type, and names the origin', () => {
    for (const type of ['epic', 'reflection', 'story', 'task', 'bug'] as const) {
      const html = face(card({ type, contribution: record }));
      expect(html).toContain(`hatch-type-glyph--${type}`);
      expect(html).toContain('hatch-contribution-mark');
      expect(html).toContain('HA-9');
      expect(html).toContain('hatch.example.org');
    }
  });

  it('names @login where the record has no origin key', () => {
    const html = face(card({ contribution: { ...record, originKey: null, originInstance: null } }));
    expect(html).toContain('@someone');
  });

  it('nests no <a inside the face anchor', () => {
    const c = card({ contribution: record });
    const html = renderSortable(<BoardCard card={c} />, [c.key]);
    for (const [start, end] of anchorSpans(html)) {
      expect(html.slice(start, end).indexOf('<a ', 1)).toBe(-1);
    }
  });
});

describe('BoardCard, carrying stories', () => {
  const epic = card({ type: 'epic', key: 'AER-1' });
  const statuses = [
    status(1, 0, { name: 'Backlog', color: '#111111' }),
    status(2, 1, { name: 'Doing', color: '#222222' }),
    status(3, 2, { name: 'Done', color: '#333333', isTerminal: true }),
  ];
  const stories = [
    card({ key: 'AER-2', type: 'story', parentKey: 'AER-1', statusId: 1, title: 'First story' }),
    card({ key: 'AER-3', type: 'story', parentKey: 'AER-1', statusId: 2, title: 'Second story' }),
    card({ key: 'AER-4', type: 'story', parentKey: 'AER-1', statusId: 3, title: 'Third story' }),
  ];

  it('draws one line per story, in order, each with its own column name', () => {
    const html = renderSortable(<BoardCard card={epic} stories={stories} statuses={statuses} />, [epic.key]);

    expect(html.split('class="hatch-card-story-line"').length - 1).toBe(stories.length);
    const indices = stories.map((s) => html.indexOf(`>${s.title}<`));
    for (const i of indices) expect(i).toBeGreaterThanOrEqual(0);
    for (let i = 1; i < indices.length; i++) expect(indices[i]).toBeGreaterThan(indices[i - 1]);

    for (const [story, st] of [
      [stories[0], statuses[0]],
      [stories[1], statuses[1]],
      [stories[2], statuses[2]],
    ] as const) {
      expect(html).toContain(`title="${story.key} - ${st.name}"`);
      expect(html).toContain(`>${st.name}<`);
    }
  });

  it('cuts a 300-character title with the ellipsis character truncate() appends', () => {
    const longTitle = 'x'.repeat(300);
    const longStory = card({ key: 'AER-5', type: 'story', parentKey: 'AER-1', statusId: 1, title: longTitle });
    const html = renderSortable(
      <BoardCard card={epic} stories={[longStory]} statuses={statuses} />,
      [epic.key],
    );

    expect(html).not.toContain(longTitle);
    expect(html).toContain('…');
    expect(html).not.toContain('...');
  });

  it('renders the toggle collapsed, with no story lines', () => {
    const html = renderSortable(
      <BoardCard card={epic} stories={stories} statuses={statuses} collapsed />,
      [epic.key],
    );

    const buttonStart = html.indexOf('<button');
    expect(buttonStart).toBeGreaterThanOrEqual(0);
    expect(html.slice(buttonStart, html.indexOf('</button>'))).toContain('aria-expanded="false"');
    expect(html).not.toContain('hatch-card-story-lines');
  });

  it('renders the toggle expanded, with the story lines, by default', () => {
    const html = renderSortable(<BoardCard card={epic} stories={stories} statuses={statuses} />, [epic.key]);

    const buttonStart = html.indexOf('<button');
    expect(buttonStart).toBeGreaterThanOrEqual(0);
    expect(html.slice(buttonStart, html.indexOf('</button>'))).toContain('aria-expanded="true"');
    expect(html).toContain('hatch-card-story-lines');
  });

  describe.each([
    ['an open question', { openQuestions: 1 }],
    ['a hold', { held: true }],
  ])('a story with %s', (_name, over) => {
    const needing = [stories[0], card({ ...stories[1], ...over }), stories[2]];

    it('draws its dot and icon as a "!" mark and its line as Needs me', () => {
      expect(needsMe(needing[1])).toBe(true);
      const html = renderSortable(<BoardCard card={epic} stories={needing} statuses={statuses} />, [epic.key]);

      expect(html.split('hatch-card-story-dot--needs-me').length - 1).toBe(1);
      expect(html.split('hatch-card-story-icon--needs-me').length - 1).toBe(1);
      expect(html.split('hatch-card-story-column--needs-me').length - 1).toBe(1);
      expect(html).toContain('>Needs me<');
      expect(html).toContain('title="AER-3 - Doing - needs me"');
      expect(html).toContain(`· 1 needs me`);
      expect(html).toContain(`aria-label="${stripLabel(3, 1, 1)}"`);
expect(html).toContain(`title="${stripLabel(3, 1, 1)}"`);
      expect(html).toContain('aria-label="3 stories, 1 done, 1 needs you"');
    });

    it('leaves the other stories as they were', () => {
      const html = renderSortable(<BoardCard card={epic} stories={needing} statuses={statuses} />, [epic.key]);

      expect(html).toContain('title="AER-2 - Backlog"');
      expect(html).toContain('title="AER-4 - Done"');
      expect(html).toContain('>Backlog<');
      expect(html).toContain('>Done<');
      expect(html).not.toContain('>Doing<');
    });

    it('keeps the dot and the count when collapsed, with no lines', () => {
      const html = renderSortable(
        <BoardCard card={epic} stories={needing} statuses={statuses} collapsed />,
        [epic.key],
      );

      expect(html).toContain('hatch-card-story-dot--needs-me');
      expect(html).toContain('· 1 needs me');
      expect(html).not.toContain('hatch-card-story-lines');
    });
  });

  it('marks nothing when no story is waiting on anyone', () => {
    const html = renderSortable(<BoardCard card={epic} stories={stories} statuses={statuses} />, [epic.key]);

    expect(html).not.toContain('needs-me');
    expect(html).not.toContain('Needs me');
    expect(html).not.toContain('needs you');
    expect(html).toContain('aria-label="3 stories, 1 done"');
  });

  it('nests no <a inside the face anchor, and no <button inside any <a', () => {
    const html = renderSortable(<BoardCard card={epic} stories={stories} statuses={statuses} />, [epic.key]);

    const spans = anchorSpans(html);
    expect(spans.length).toBeGreaterThan(0);
    for (const [start, end] of spans) {
      const inner = html.slice(start, end);
      expect(inner.indexOf('<a ', 1)).toBe(-1);
      expect(inner).not.toContain('<button');
    }
  });

  it('carries no hatch-card--carrying on a plain card or a stories-less epic', () => {
    const plainCard = card({ key: 'AER-9' });
    const plain = renderSortable(<BoardCard card={plainCard} />, [plainCard.key]);
    expect(plain).not.toContain('hatch-card--carrying');

    const bareEpic = renderSortable(<BoardCard card={epic} stories={[]} statuses={statuses} />, [epic.key]);
    expect(bareEpic).not.toContain('hatch-card--carrying');
  });
});

describe('lineClick', () => {
  const story = card({ key: 'AER-2', type: 'story', parentKey: 'AER-1' });

  it('calls onPeek with the story and prevents default on a plain click', () => {
    const onPeek = vi.fn();
    const preventDefault = vi.fn();
    lineClick(story, onPeek)({ button: 0, preventDefault });

    expect(onPeek).toHaveBeenCalledWith(story);
    expect(preventDefault).toHaveBeenCalled();
  });

  it('does neither on a ctrl-click', () => {
    const onPeek = vi.fn();
    const preventDefault = vi.fn();
    lineClick(story, onPeek)({ button: 0, ctrlKey: true, preventDefault });

    expect(onPeek).not.toHaveBeenCalled();
    expect(preventDefault).not.toHaveBeenCalled();
  });

  it('does neither with no onPeek', () => {
    const preventDefault = vi.fn();
    lineClick(story, undefined)({ button: 0, preventDefault });

    expect(preventDefault).not.toHaveBeenCalled();
  });
});
