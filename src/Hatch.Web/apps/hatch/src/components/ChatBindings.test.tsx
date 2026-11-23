import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { BindingList, ChatBindings } from './ChatBindings';
import type { ChatWorkspace, Project } from '../types';

// The client reaches lib/clientLogger, which reads `window` at import, and
// there is no jsdom here.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const project = (id: number, key: string, name: string): Project => ({ id, key, name }) as Project;
const PROJECTS = [project(1, 'AER', 'Aerie'), project(2, 'BEE', 'Hive'), project(3, 'CHA', 'Chat')];

const workspace = (projectIds: number[]): ChatWorkspace => ({
  id: 7,
  provider: 'slack',
  externalId: 'T7',
  name: 'Fellowship',
  hasToken: true,
  createdAt: '2026-03-01T11:00:00Z',
  projectIds,
});

const noop = () => {};

describe('the binding list', () => {
  const list = (projectIds: number[], disabled = false, failure: string | null = null) =>
    renderToStaticMarkup(
      <BindingList workspace={workspace(projectIds)} projects={PROJECTS} disabled={disabled} failure={failure} onToggle={noop} />,
    );

  it('offers every project the reader can see, by key and name', () => {
    const html = list([]);
    for (const text of ['AER', 'Aerie', 'BEE', 'Hive', 'CHA', 'Chat']) expect(html).toContain(text);
    expect(html.match(/type="checkbox"/g)).toHaveLength(3);
  });

  it('ticks only the projects the workspace is bound to', () => {
    const html = list([1, 3]);
    expect(html.match(/checked=""/g)).toHaveLength(2);
  });

  it('disables the whole list while a write waits', () => {
    expect(list([1], true).match(/disabled=""/g)).toHaveLength(3);
    expect(list([1], false)).not.toContain('disabled=""');
  });

  it('shows a refusal as the sentence the server gave, under the list', () => {
    const html = list([], false, 'that project could not be found');
    expect(html).toContain('that project could not be found');
    expect(html).toContain('text-danger');
  });

  it('says so, and draws no checkbox, where there is no project to bind', () => {
    const html = renderToStaticMarkup(
      <BindingList workspace={workspace([])} projects={[]} disabled={false} failure={null} onToggle={noop} />,
    );
    expect(html).toContain('No project to bind yet.');
    expect(html).not.toContain('checkbox');
  });
});

describe('the bindings control', () => {
  it('starts enabled and quiet, and names the workspace it binds', () => {
    const html = renderToStaticMarkup(
      <ChatBindings workspace={workspace([2])} projects={PROJECTS} busy={false} onChange={noop} />,
    );
    expect(html).toContain('aria-label="Projects bound to Fellowship"');
    expect(html).not.toContain('text-danger');
    expect(html).not.toContain('disabled=""');
  });

  it('is disabled while the page is busy', () => {
    const html = renderToStaticMarkup(
      <ChatBindings workspace={workspace([])} projects={PROJECTS} busy onChange={noop} />,
    );
    expect(html.match(/disabled=""/g)).toHaveLength(3);
  });
});
