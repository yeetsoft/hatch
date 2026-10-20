import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { ChangelogBody } from './ChangelogPage';
import { appendWindow } from '../lib/changelog';
import type { ChangelogDayDto, ChangelogDto, ChangelogEntryDto, Project } from '../types';

/* No DOM runner, so the body is rendered from props, the way
   TimelinePage.test.tsx does it. renderMarkdown's DOMPurify pair needs a real
   DOM to sanitize into, which this workspace's tests do not have - the same
   stub ProseClamp.test.tsx uses, turned into a minimal markdown renderer so a
   feature's `**bold**` note still proves it rendered. */
vi.mock('../lib/markdown', () => ({
  renderMarkdown: (source: string) => source.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>'),
}));

const project = (over: Partial<Project> = {}): Project =>
  ({
    id: 1,
    key: 'HA',
    name: 'Hatch',
    issueCount: 0,
    createdAt: '2026-01-01T00:00:00.000Z',
    color: '#3366cc',
    icon: null,
    logoUpdatedAt: null,
    repositories: [],
    members: [],
    canApprove: true,
    canAdminister: true,
    ...over,
  }) as Project;

const entry = (key: string, over: Partial<ChangelogEntryDto> = {}): ChangelogEntryDto => ({
  key,
  title: `title ${key}`,
  type: 'story',
  projectKey: 'HA',
  releaseNote: null,
  closedAt: '2026-10-13T00:00:00.000Z',
  descendantCount: 0,
  ...over,
});

const day = (date: string, over: Partial<ChangelogDayDto> = {}): ChangelogDayDto => ({
  date,
  features: [],
  fixes: [],
  ...over,
});

const changelog = (days: ChangelogDayDto[]): ChangelogDto => ({
  days,
  hasOlder: false,
  oldest: null,
  months: [],
});

const render = (dto: ChangelogDto, projects: Project[] = [project()]): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <ChangelogBody changelog={dto} projects={projects} onOlder={() => {}} />
    </MemoryRouter>,
  );

describe('ChangelogBody', () => {
  it('draws two days as two headings, in the given order', () => {
    const html = render(changelog([day('2026-10-13'), day('2026-10-12')]));

    const first = html.indexOf('October 13');
    const second = html.indexOf('October 12');
    expect(first).toBeGreaterThan(-1);
    expect(second).toBeGreaterThan(first);
  });

  it("draws a feature's key, beneath label and the note's rendered markup", () => {
    const html = render(
      changelog([
        day('2026-10-13', {
          features: [entry('HA-1', { releaseNote: '**bold** note', descendantCount: 4 })],
        }),
      ]),
    );

    expect(html).toContain('HA-1');
    expect(html).toContain('4 beneath');
    expect(html).toContain('<strong>bold</strong>');
  });

  it('still draws the day when features is empty and fixes is not', () => {
    const html = render(
      changelog([day('2026-10-13', { features: [], fixes: [entry('HA-2')] })]),
    );

    expect(html).toContain('October 13');
    expect(html).toContain('HA-2');
    expect(html).not.toContain('Nothing has shipped yet.');
  });

  it('draws the empty state when there are no days', () => {
    const html = render(changelog([]));

    expect(html).toContain('Nothing has shipped yet.');
  });

  it('draws no Older when the answer says there is nothing older', () => {
    expect(render(changelog([day('2026-10-13')]))).not.toContain('Older');
  });

  it('draws Older when the answer says there is an older window', () => {
    const html = render({ ...changelog([day('2026-10-13')]), hasOlder: true, oldest: '2026-10-13' });
    expect(html).toContain('Older');
  });

  it('draws two appended windows, the older one last', () => {
    const first = { ...changelog([day('2026-10-13')]), hasOlder: true, oldest: '2026-10-13' };
    const second = changelog([day('2026-10-02')]);

    const html = render(appendWindow(first, second));

    const a = html.indexOf('October 13');
    const b = html.indexOf('October 2,');
    expect(a).toBeGreaterThan(-1);
    expect(b).toBeGreaterThan(a);
  });

  it('draws the months as links ahead of the first day', () => {
    const html = render({
      ...changelog([day('2026-10-13')]),
      months: [
        { month: '2026-10', count: 3 },
        { month: '2026-04', count: 1 },
      ],
    });

    expect(html).toContain('href="/changelog?before=2026-11-01"');
    expect(html).toContain('href="/changelog?before=2026-05-01"');
    expect(html).toContain('2026 · 3');
    expect(html).toContain('2026 · 1');
    expect(html.indexOf('hatch-changelog-months')).toBeLessThan(html.indexOf('hatch-changelog-day'));
  });

  it('draws no month list when there are no months', () => {
    expect(render(changelog([day('2026-10-13')]))).not.toContain('hatch-changelog-months');
  });
});
