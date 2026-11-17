import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PendingDialog, ScopeChecklist, ScopeSelect, UsersPage } from './UsersPage';
import type { Person, PersonScope, Project } from '../types';

// The client reaches lib/clientLogger, which reads `window` at import, and
// there is no jsdom here.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const project = (id: number, key: string, name: string, color: string | null = '#3366cc'): Project =>
  ({ id, key, name, color, icon: null }) as Project;

const PROJECTS = [project(1, 'AER', 'Aerial'), project(2, 'BRG', 'Bridge', '#cc6633'), project(3, 'CLD', 'Cloud', null)];

const ADA: Person = {
  id: 'ada',
  name: 'Ada',
  role: 'admin',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  photoUpdatedAt: null,
  sessionCount: 0,
  email: null,
  provider: null,
  lastSignInAt: null,
};

const scope = (mode: PersonScope['mode'], projectIds: number[] = []): PersonScope => ({ personId: 'ada', mode, projectIds });

describe('the Scope select on a row', () => {
  it('offers none, all and some, with the saved mode selected', () => {
    const html = renderToStaticMarkup(<ScopeSelect name="Ada" scope={scope('all', [1, 2, 3])} disabled={false} onChoose={() => {}} />);
    expect(html).toContain('aria-label="Scope of Ada"');
    expect(html.indexOf('>none<')).toBeLessThan(html.indexOf('>all<'));
    expect(html.indexOf('>all<')).toBeLessThan(html.indexOf('>some<'));
    expect(html).toMatch(/<option value="all" selected="">all<\/option>/);
  });

  it('reads some (N) with the ticked count, and some (0) with none', () => {
    const two = renderToStaticMarkup(<ScopeSelect name="Ada" scope={scope('some', [1, 2])} disabled={false} onChoose={() => {}} />);
    expect(two).toMatch(/<option value="some" selected="">some \(2\)<\/option>/);
    const zero = renderToStaticMarkup(<ScopeSelect name="Ada" scope={scope('some')} disabled={false} onChoose={() => {}} />);
    expect(zero).toContain('some (0)');
  });

  it('is disabled while a write is in flight', () => {
    const html = renderToStaticMarkup(<ScopeSelect name="Ada" scope={scope('none')} disabled onChoose={() => {}} />);
    expect(html).toContain('disabled=""');
  });
});

describe('the project checklist', () => {
  it('lists every project with its key and name, and ticks the selected ones', () => {
    const html = renderToStaticMarkup(<ScopeChecklist projects={PROJECTS} selected={[2]} disabled={false} onChange={() => {}} />);
    expect(html.match(/type="checkbox"/g)).toHaveLength(3);
    expect(html.match(/checked=""/g)).toHaveLength(1);
    for (const p of PROJECTS) {
      expect(html).toContain(`${p.key} ${p.name}`);
      expect(html).toContain(`title="${p.name}"`);
    }
  });

  it('is disabled while busy', () => {
    const html = renderToStaticMarkup(<ScopeChecklist projects={PROJECTS} selected={[]} disabled onChange={() => {}} />);
    expect(html.match(/disabled=""/g)).toHaveLength(3);
  });

  it('says so while the projects are still being read', () => {
    expect(renderToStaticMarkup(<ScopeChecklist projects={null} selected={[]} disabled={false} onChange={() => {}} />)).toContain(
      'Reading projects',
    );
  });
});

describe('the narrowing confirmation', () => {
  it('names the change, says what is lost, and offers Leave it and Narrow', () => {
    const html = renderToStaticMarkup(
      <PendingDialog
        pending={{ kind: 'narrow', person: ADA, from: scope('all', [1, 2, 3]), to: scope('none') }}
        busy={false}
        onClose={() => {}}
        onConfirm={() => {}}
      />,
    );
    expect(html).toContain('Change Ada&#x27;s scope?');
    expect(html).toContain('Ada goes from all to none: they will stop seeing 3 projects.');
    expect(html).toContain('Leave it');
    expect(html).toContain('Narrow');
  });

  it('keeps the role wording for a demotion', () => {
    const html = renderToStaticMarkup(
      <PendingDialog pending={{ kind: 'demote', person: ADA, to: 'user' }} busy={false} onClose={() => {}} onConfirm={() => {}} />,
    );
    expect(html).toContain('Change Ada&#x27;s role?');
    expect(html).toContain('Demote');
  });
});

describe('the Add user form', () => {
  it('offers Scope, on all until changed, with no checklist', () => {
    const html = renderToStaticMarkup(<UsersPage />);
    expect(html).toContain('Scope');
    expect(html).toMatch(/<option value="all" selected="">all<\/option>/);
    expect(html).not.toContain('type="checkbox"');
  });
});
