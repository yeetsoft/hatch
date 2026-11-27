import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { NewAmbitionDialog, NewAmbitionForm } from './NewAmbitionDialog';
import type { Project } from '../types';

const project = (id: number, key: string): Project => ({ id, key, name: key, color: '#3366cc' }) as Project;

const draw = (projects: Project[], open = true) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <NewAmbitionDialog open={open} projects={projects} onClose={() => {}} />
    </MemoryRouter>,
  );

describe('NewAmbitionDialog', () => {
  it('draws nothing while closed', () => {
    expect(draw([project(1, 'AER')], false)).toBe('');
  });

  it('is titled New ambition and holds the words, the picker and no dreams', () => {
    const html = draw([project(1, 'AER'), project(2, 'OPS')]);

    expect(html).toContain('New ambition');
    expect(html).toContain('aria-label="Words"');
    expect(html).toContain('aria-label="Add images or videos"');
    expect(html).toContain('Shape');
    expect(html).not.toContain('No dreams go in.');
    expect(html).not.toContain('a dream');
  });

  it('with two projects and none picked draws both tiles and asks for a pick', () => {
    const html = draw([project(1, 'AER'), project(2, 'OPS')]);

    expect(html).toContain('Project');
    expect(html).toContain('AER');
    expect(html).toContain('OPS');
    expect(html).not.toContain('checked');
    expect(html).toContain('Pick a project above to shape.');
  });

  it('with one project has it checked and asks for nothing more than words', () => {
    const html = draw([project(1, 'AER')]);

    expect(html).toContain('checked');
    expect(html).not.toContain('Pick a project above to shape.');
    expect(html).toContain('Give some words or a file first.');
  });

  it('with no project says there is none to shape into and draws no picker', () => {
    const html = draw([]);

    expect(html).toContain('There is no project to shape into yet.');
    expect(html).not.toContain('hatch-project-chooser');
  });
});

describe('NewAmbitionForm', () => {
  it('draws the same form without the dialog around it', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <NewAmbitionForm projects={[project(1, 'AER')]} />
      </MemoryRouter>,
    );

    expect(html).toContain('aria-label="Words"');
  });
});
