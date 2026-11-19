import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MoveProjectDialog } from './MoveProjectDialog';
import { moveSummary } from '../lib/moveProject';
import type { MoveProject } from '../lib/useMoveProject';
import type { Project } from '../types';

const project = (id: number, key: string, name: string): Project => ({ id, key, name }) as Project;
const projects = [project(1, 'AER', 'Aerie'), project(2, 'OPS', 'Operations')];

const child = (key: string) => ({ key, title: `${key} title`, claim: null, pullRequestUrl: null });

/* A story under an epic, with two tasks: what the dialog reads once the
   descendants are in and a project is chosen. */
const move = (over: { parentKey?: string | null; descendants?: number; moveDescendants?: boolean } = {}): MoveProject => {
  const issue = { ...child('AER-2'), projectId: 1, parentKey: over.parentKey === undefined ? 'AER-1' : over.parentKey };
  const descendants = [child('AER-3'), child('AER-4')].slice(0, over.descendants ?? 2);
  const moveDescendants = over.moveDescendants ?? true;
  return {
    offer: { issue, descendants, projectId: 2, moveDescendants },
    loading: false,
    busy: false,
    error: null,
    summary: moveSummary(issue, descendants, moveDescendants),
    open: vi.fn(),
    setProjectId: vi.fn(),
    setMoveDescendants: vi.fn(),
    close: vi.fn(),
    confirm: vi.fn(),
  };
};

const text = (m: MoveProject) =>
  renderToStaticMarkup(<MoveProjectDialog move={m} projects={projects} />)
    .replace(/<!-- -->/g, '')
    .replace(/<[^>]+>/g, ' ')
    .replace(/\s+/g, ' ');

describe('MoveProjectDialog', () => {
  it('says the parent stays, and never that it is left behind', () => {
    const shown = text(move());

    expect(shown).toContain('It stays under AER-1.');
    expect(shown).not.toContain('no longer sit under');
  });

  it('says nothing about a parent when there is none', () => {
    expect(text(move({ parentKey: null }))).not.toContain('It stays under');
  });

  it('offers to carry the children along, re-keyed, or to leave them under this issue', () => {
    const shown = text(move());

    expect(shown).toContain('Move the 2 issues under it too, with new keys');
    expect(shown).toContain('Leave them in Aerie, still under this issue');
    expect(shown).not.toContain('no parent');
  });

  it('draws no choice for an issue with nothing under it', () => {
    expect(text(move({ descendants: 0 }))).not.toContain('still under this issue');
  });
});
