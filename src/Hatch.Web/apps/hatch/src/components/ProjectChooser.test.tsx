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
    members: [],
    canApprove: false,
    canAdminister: false,
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

  it('disables only the tile matching disabledId', () => {
    const projects = [project({ id: 1, key: 'HA', name: 'Hatch' }), project({ id: 2, key: 'AER', name: 'Aerie' })];
    const html = renderToStaticMarkup(
      <ProjectChooser projects={projects} value={null} onChange={noop} legend="Project" disabledId={2} />,
    );

    const disabled = [...html.matchAll(/disabled=""/g)];
    expect(disabled).toHaveLength(1);
    expect(html.indexOf('disabled=""')).toBeLessThan(html.indexOf('title="Aerie"'));
  });

  it('disables no tile when disabledId is omitted', () => {
    const projects = [project({ id: 1, key: 'HA', name: 'Hatch' }), project({ id: 2, key: 'AER', name: 'Aerie' })];
    const html = renderToStaticMarkup(
      <ProjectChooser projects={projects} value={null} onChange={noop} legend="Project" />,
    );

    expect(html).not.toContain('disabled=""');
  });

  it('renders a Keep tile first, checked, when keepLabel is set and value is null', () => {
    const projects = [project({ id: 1, key: 'HA', name: 'Hatch' }), project({ id: 2, key: 'AER', name: 'Aerie' })];
    const html = renderToStaticMarkup(
      <ProjectChooser projects={projects} value={null} onChange={noop} legend="Project" keepLabel="Keep" onKeep={noop} />,
    );

    expect(html.indexOf('Keep')).toBeLessThan(html.indexOf('title="Hatch"'));
    const checked = [...html.matchAll(/checked=""/g)];
    expect(checked).toHaveLength(1);
    expect(html.indexOf('checked=""')).toBeLessThan(html.indexOf('title="Hatch"'));
  });

  it('checks a project tile instead of Keep once a project is chosen', () => {
    const projects = [project({ id: 1, key: 'HA', name: 'Hatch' }), project({ id: 2, key: 'AER', name: 'Aerie' })];
    const html = renderToStaticMarkup(
      <ProjectChooser projects={projects} value={1} onChange={noop} legend="Project" keepLabel="Keep" onKeep={noop} />,
    );

    const checked = [...html.matchAll(/checked=""/g)];
    expect(checked).toHaveLength(1);
    expect(html.indexOf('checked=""')).toBeGreaterThan(html.indexOf('Keep'));
  });

  it('omits the Keep tile when keepLabel is not passed', () => {
    const projects = [project({ id: 1, key: 'HA', name: 'Hatch' }), project({ id: 2, key: 'AER', name: 'Aerie' })];
    const html = renderToStaticMarkup(
      <ProjectChooser projects={projects} value={null} onChange={noop} legend="Project" />,
    );

    expect(html).not.toContain('Keep');
  });
});
