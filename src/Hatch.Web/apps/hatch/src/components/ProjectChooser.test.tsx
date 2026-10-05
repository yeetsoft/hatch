import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { Project } from '../types';
import { ProjectChooser } from './ProjectChooser';

const noop = () => {};

function project(overrides: Partial<Project>): Project {
  return {
    id: 1,
    key: 'HA',
    name: 'Hatch',
    issueCount: 0,
    createdAt: '2026-01-01T00:00:00Z',
    color: null,
    icon: null,
    logoUpdatedAt: null,
    repositories: [],
    ...overrides,
  };
}

describe('ProjectChooser', () => {
  it('draws one label per project', () => {
    const projects = [project({ id: 1, key: 'HA', name: 'Hatch' }), project({ id: 2, key: 'AER', name: 'Aerie' })];
    const html = renderToStaticMarkup(
      <ProjectChooser projects={projects} value={null} onChange={noop} legend="Project" />,
    );

    const labels = [...html.matchAll(/<label /g)];
    expect(labels).toHaveLength(2);
    expect(html).toContain('>Project<');
  });

  it('checks exactly the tile matching value', () => {
    const projects = [project({ id: 1, key: 'HA', name: 'Hatch' }), project({ id: 2, key: 'AER', name: 'Aerie' })];
    const html = renderToStaticMarkup(
      <ProjectChooser projects={projects} value={2} onChange={noop} legend="Project" />,
    );

    const checked = [...html.matchAll(/checked=""/g)];
    expect(checked).toHaveLength(1);
    expect(html.indexOf('checked=""')).toBeLessThan(html.indexOf('title="Aerie"'));
  });

  it('renders a single project as one checked tile', () => {
    const projects = [project({ id: 1, key: 'HA', name: 'Hatch' })];
    const html = renderToStaticMarkup(
      <ProjectChooser projects={projects} value={1} onChange={noop} legend="Project" />,
    );

    const labels = [...html.matchAll(/<label /g)];
    expect(labels).toHaveLength(1);
    const checked = [...html.matchAll(/checked=""/g)];
    expect(checked).toHaveLength(1);
  });

  it('shows each project key and name as text', () => {
    const projects = [project({ id: 1, key: 'HA', name: 'Hatch' }), project({ id: 2, key: 'AER', name: 'Aerie' })];
    const html = renderToStaticMarkup(
      <ProjectChooser projects={projects} value={null} onChange={noop} legend="Project" />,
    );

    expect(html).toContain('HA');
    expect(html).toContain('Hatch');
    expect(html).toContain('AER');
    expect(html).toContain('Aerie');
  });
});
