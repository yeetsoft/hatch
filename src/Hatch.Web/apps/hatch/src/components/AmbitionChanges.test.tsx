import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { AmbitionChangesSection } from './AmbitionChanges';
import { AmbitionsBody } from '../pages/AmbitionsPage';
import type { AmbitionsView } from '../pages/AmbitionsPage';
import type { AmbitionChangeLine, AmbitionChanges, AmbitionRow } from '../types';

/* No DOM runner: the section is rendered from props. */
const NOW = new Date('2026-11-20T12:00:00Z');

const line = (kind: AmbitionChangeLine['kind'], over: Partial<AmbitionChangeLine> = {}): AmbitionChangeLine => ({
  kind,
  at: '2026-11-20T11:00:00Z',
  ambitionKey: 'AER-1',
  ambitionTitle: 'Partners order alone',
  issueKey: 'AER-2',
  issueTitle: 'A partner order form',
  statement: null,
  lookedAt: null,
  serves: null,
  wentAheadOn: null,
  question: null,
  count: null,
  ...over,
});

const changes = (over: Partial<AmbitionChanges> = {}): AmbitionChanges => ({
  since: '2026-11-20T09:00:00Z',
  basis: 'visit',
  omitted: 0,
  lines: [],
  ...over,
});

const draw = (read: AmbitionChanges | null, error: string | null = null, scopeKeys: string[] = []): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <AmbitionChangesSection changes={read} error={error} scopeKeys={scopeKeys} now={NOW} />
    </MemoryRouter>,
  );

/** The markup of each line's link, in order. */
const links = (html: string): string[] => html.match(/<a class="hatch-ambition-changes-line".*?<\/a>/g) ?? [];

/** The text a link reads as, one space between its parts. */
const words = (markup: string): string => markup.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim();

describe('AmbitionChangesSection', () => {
  it('draws as a card headed Since you last looked, with how long ago that was', () => {
    const html = draw(changes());

    expect(html).toContain('<section class="hatch-card hatch-ambition-changes">');
    expect(html).toContain('<h2 class="hatch-ambition-heading">Since you last looked</h2>');
    expect(html).toContain('<span class="text-muted">3 hours ago</span>');
    expect(html).not.toContain('first visit');
  });

  it('draws a holds line: the statement, the ambition and what was looked at, linking to the ambition', () => {
    const [drawn] = links(
      draw(
        changes({
          lines: [
            line('holds', {
              issueKey: 'AER-1',
              issueTitle: 'Partners order alone',
              statement: 'A partner can place an order',
              lookedAt: 'the staging flow',
            }),
          ],
        }),
      ),
    );

    expect(drawn).toContain('href="/issues/AER-1"');
    expect(words(drawn)).toBe(
      'Now holds A partner can place an order Partners order alone Looked at: the staging flow',
    );
  });

  it('draws a carved line: the key, the title and what it serves, linking to the epic', () => {
    const [serving, bare] = links(
      draw(changes({ lines: [line('carved', { serves: [1, 2] }), line('carved', { issueKey: 'AER-3', serves: [] })] })),
    );

    expect(serving).toContain('href="/issues/AER-2"');
    expect(words(serving)).toBe('Carved AER-2 A partner order form serves #1, #2');
    expect(words(bare)).toBe('Carved AER-3 A partner order form serves no capability yet');
  });

  it('draws a decided line: the key and what it went ahead on, linking to the Ballot', () => {
    const [drawn] = links(draw(changes({ lines: [line('decided', { wentAheadOn: 'One form for both' })] })));

    expect(drawn).toContain('href="/ballot"');
    expect(words(drawn)).toBe('Decided without you AER-2 One form for both');
  });

  it('says so where a decision names nothing it went ahead on', () => {
    const [drawn] = links(draw(changes({ lines: [line('decided')] })));

    expect(words(drawn)).toBe('Decided without you AER-2 Nothing was named to go ahead on.');
  });

  it("carries the tab's scope to the Ballot", () => {
    const [drawn] = links(draw(changes({ lines: [line('decided')] }), null, ['AER', 'OPS']));

    expect(drawn).toContain('href="/ballot?scope=AER,OPS"');
  });

  it("draws an asked line: the key and the question's first line, or a count where several wait", () => {
    const [one, several] = links(
      draw(
        changes({
          lines: [
            line('asked', { question: 'Which form should it take?', count: 1 }),
            line('asked', { issueKey: 'AER-3', question: 'And the colour?', count: 3 }),
          ],
        }),
      ),
    );

    expect(one).toContain('href="/issues/AER-2"');
    expect(words(one)).toBe('Asked you AER-2 Which form should it take?');
    expect(words(several)).toBe('Asked you AER-3 3 questions are waiting');
    expect(several).not.toContain('And the colour?');
  });

  it('keeps the lines in the order the read gave them, each a list item', () => {
    const html = draw(changes({ lines: [line('asked', { question: 'q' }), line('holds', { statement: 's' })] }));

    expect(html.match(/<li>/g)).toHaveLength(2);
    expect(html.indexOf('Asked you')).toBeLessThan(html.indexOf('Now holds'));
  });

  it('says a first visit covers the last seven days, and gives no time since a look that never was', () => {
    const html = draw(changes({ basis: 'week', since: '2026-11-13T12:00:00Z', lines: [line('carved')] }));

    expect(html).toContain('This is your first visit, so this covers the last seven days.');
    expect(html).not.toContain('days ago');
    expect(links(html)).toHaveLength(1);
  });

  it('is one sentence where nothing changed', () => {
    const html = draw(changes());

    expect(html).toContain('Nothing has changed since you last looked.');
    expect(html).toContain('hatch-empty');
    expect(html).not.toContain('hatch-ambition-changes-lines');
  });

  it('ends with how many more there were, only where some were left out', () => {
    const html = draw(changes({ omitted: 4, lines: [line('carved')] }));

    expect(html).toMatch(/<li class="hatch-ambition-changes-omitted">and 4 more<\/li><\/ul>/);
    expect(draw(changes({ lines: [line('carved')] }))).not.toContain(' more');
  });

  it('says the read failed, in place of lines and the empty sentence', () => {
    const html = draw(null, 'boom');

    expect(html).toContain(
      '<p class="text-danger hatch-ambition-failure">What changed since you last looked could not be read: boom</p>',
    );
    expect(html).toContain('Since you last looked</h2>');
    expect(html).not.toContain('Nothing has changed');
    expect(html).not.toContain(' ago');
  });
});

describe('the section on the page', () => {
  const ambition: AmbitionRow = {
    key: 'AER-1',
    projectKey: 'AER',
    title: 'Partners order alone',
    statusId: 1,
    statusName: 'To Do',
    stage: 'ready',
    stageWhy: 'Nothing runs until a person starts it.',
    limit: 1,
    capabilities: 0,
    holds: 0,
    inFlight: 0,
    waitsOnPerson: 0,
    notStarted: 0,
    newlyHolding: 0,
    open: [],
    summary: '',
    decisions: 0,
    budget: { budgetUsd: null, spentUsd: 0, unpricedSessions: 0, spent: false },
    childBeyondScope: false,
  };

  const page = (over: Partial<AmbitionsView> = {}) =>
    renderToStaticMarkup(
      <MemoryRouter>
        <AmbitionsBody
          data={{
            ambitions: [ambition],
            statuses: [],
            ballot: { roots: [], questions: [] },
            ballotError: null,
            changes: changes(),
            changesError: null,
            scopeKey: '',
            ...over,
          }}
          scopeKeys={[]}
          now={NOW}
        />
      </MemoryRouter>,
    );

  it('sits below the rows', () => {
    const html = page();

    expect(html.indexOf('hatch-ambition-row')).toBeLessThan(html.indexOf('hatch-ambition-changes'));
  });

  it('says its own failure and leaves the tiles and the rows drawn', () => {
    const html = page({ changes: null, changesError: 'boom' });

    expect(html).toContain('What changed since you last looked could not be read: boom');
    expect(html).toContain('Waiting on you');
    expect(html).toContain('Capabilities that hold');
    expect(html).toContain('hatch-ambition-row');
  });
});
