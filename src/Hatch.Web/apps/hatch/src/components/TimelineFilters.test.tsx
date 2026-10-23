import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { Project } from '../types';
import { TimelineFilters } from './TimelineFilters';

const projectOf = (key: string, name: string): Project => ({
  id: 1,
  key,
  name,
  issueCount: 0,
  createdAt: '2026-01-01T00:00:00.000Z',
  color: null,
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
  members: [],
  canApprove: false,
  canAdminister: false,
  landing: 'pull-request',
  checksRequired: false,
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
});

const ONE = [projectOf('AER', 'Aero')];
const TWO = [projectOf('AER', 'Aero'), projectOf('HA', 'Hatch')];

function render({
  projects = TWO,
  project = '',
  creators = ['Ada', 'Grace'],
  creator = '',
}: { projects?: Project[]; project?: string; creators?: string[]; creator?: string } = {}) {
  return renderToStaticMarkup(
    <TimelineFilters
      projects={projects}
      project={project}
      creators={creators}
      creator={creator}
      onProject={() => {}}
      onCreator={() => {}}
    />,
  );
}

const shown = (html: string) =>
  [...html.matchAll(/<span class="hatch-facet__option hatch-facet__option--shown">([^<]*)</g)].map((m) => m[1]);

const options = (html: string) => [...html.matchAll(/<option value="[^"]*"[^>]*>([^<]*)</g)].map((m) => m[1]);

const lit = (html: string) => [...html.matchAll(/hatch-facet--lit/g)].length;

describe('TimelineFilters', () => {
  it('is a group named Filters', () => {
    expect(render()).toContain('role="group" aria-label="Filters"');
  });

  it('draws no Project facet for one project', () => {
    const html = render({ projects: ONE });

    expect(html).not.toContain('hatch-facet__label">Project<');
    expect(html).toContain('hatch-facet__label">Creator<');
  });

  it('draws the Project facet for two, All projects first, as KEY — Name', () => {
    const html = render();

    expect(html).toContain('hatch-facet__label">Project<');
    expect(options(html).slice(0, 3)).toEqual(['All projects', 'AER — Aero', 'HA — Hatch']);
    expect(shown(html)).toEqual(['All projects', 'Anyone']);
  });

  it('offers Anyone and then exactly the given creators, in order', () => {
    expect(options(render({ projects: ONE, creators: ['Grace', 'Ada'] }))).toEqual(['Anyone', 'Grace', 'Ada']);
  });

  it('keeps a creator the list does not hold as an option, and shows it', () => {
    const html = render({ projects: ONE, creator: 'Zed' });

    expect(options(html)).toEqual(['Anyone', 'Ada', 'Grace', 'Zed']);
    expect(shown(html)).toEqual(['Zed']);
  });

  it('lights neither facet by default', () => {
    expect(lit(render())).toBe(0);
  });

  it('lights each facet when its value is set', () => {
    expect(lit(render({ project: 'HA' }))).toBe(1);
    expect(lit(render({ creator: 'Ada' }))).toBe(1);
    expect(lit(render({ project: 'HA', creator: 'Ada' }))).toBe(2);
  });
});
