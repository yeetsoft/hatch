import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ProjectRow } from './ProjectRow';
import type { Project } from '../types';

const BASE: Project = {
  id: 1,
  key: 'AER',
  name: 'Aerospace',
  issueCount: 0,
  createdAt: '2026-01-01T00:00:00Z',
  color: null,
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
};

const noop = () => undefined;

describe('ProjectRow', () => {
  it('renders the key inside <code>', () => {
    const html = renderToStaticMarkup(<ProjectRow project={BASE} onEdit={noop} onDelete={noop} />);
    expect(html).toContain('<code>AER</code>');
  });

  it("draws the project's mark", () => {
    const html = renderToStaticMarkup(<ProjectRow project={BASE} onEdit={noop} onDelete={noop} />);
    expect(html).toContain('role="img"');
    expect(html).toContain(`aria-label="${BASE.name}"`);
  });

  it('shows the repository count and the primary\'s canonical, muted, when there are repositories', () => {
    const project: Project = {
      ...BASE,
      repositories: [
        { remote: 'git@github.com:yeetsoft/aer.git', canonical: 'github.com/yeetsoft/aer', baseBranch: null },
        { remote: 'git@github.com:yeetsoft/aer2.git', canonical: 'github.com/yeetsoft/aer2', baseBranch: null },
      ],
    };
    const html = renderToStaticMarkup(<ProjectRow project={project} onEdit={noop} onDelete={noop} />);
    expect(html).toContain('2');
    expect(html).toContain('class="text-muted">github.com/yeetsoft/aer<');
  });

  it('shows no muted canonical when there are no repositories', () => {
    const html = renderToStaticMarkup(<ProjectRow project={BASE} onEdit={noop} onDelete={noop} />);
    expect(html).not.toContain('text-muted');
  });

  it('disables Delete when the project has issues, and not otherwise', () => {
    const withIssues: Project = { ...BASE, issueCount: 3 };
    const busy = renderToStaticMarkup(<ProjectRow project={withIssues} onEdit={noop} onDelete={noop} />);
    const deleteButton = /<button[^>]*>Delete<\/button>/.exec(busy)![0];
    expect(deleteButton).toContain('disabled=""');

    const idle = renderToStaticMarkup(<ProjectRow project={BASE} onEdit={noop} onDelete={noop} />);
    const idleDelete = /<button[^>]*>Delete<\/button>/.exec(idle)![0];
    expect(idleDelete).not.toContain('disabled=""');
  });
});
