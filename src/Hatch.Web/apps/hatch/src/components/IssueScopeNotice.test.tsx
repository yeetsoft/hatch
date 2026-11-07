import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { IssueScopeNotice } from './IssueScopeNotice';
import { ScopeProvider } from '../lib/useScope';
import type { Project } from '../types';

// Four projects, so `?scope=AER,FIL,HA` stays a real scope: naming every
// project is naming none, and a three-project list would make the last case
// pass for the unscoped reason rather than the in-scope one.
vi.mock('../lib/useProjects', async (original) => ({
  ...(await original<typeof import('../lib/useProjects')>()),
  useProjects: () => ({
    projects: [{ key: 'AER' }, { key: 'FIL' }, { key: 'HA' }, { key: 'ZED' }] as Project[],
    byKey: new Map(),
    loaded: true,
  }),
}));

const renderAt = (entry: string, projectKey: string) =>
  renderToStaticMarkup(
    <MemoryRouter initialEntries={[entry]}>
      <ScopeProvider>
        <IssueScopeNotice projectKey={projectKey} />
      </ScopeProvider>
    </MemoryRouter>,
  );

describe('IssueScopeNotice', () => {
  it('renders nothing when the tab is unscoped', () => {
    expect(renderAt('/', 'AER')).toBe('');
    expect(renderAt('/', 'HA')).toBe('');
  });

  it('renders nothing for a project in scope', () => {
    expect(renderAt('/?scope=AER,FIL', 'AER')).toBe('');
  });

  it('names the project and offers to add it when it is out of scope', () => {
    const html = renderAt('/?scope=AER,FIL', 'HA');

    expect(html).toContain('HA');
    expect(html).toContain('is outside this tab');
    expect(html).toMatch(/Add\s*<code>HA<\/code>\s*to this tab/);
  });

  it('renders nothing once the scope has been widened to include the project', () => {
    expect(renderAt('/?scope=AER,FIL,HA', 'HA')).toBe('');
  });
});
