import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { AddTokenForm, ManifestBlock, RemoveDialog, ReplaceTokenForm, WorkspaceTable } from './ChatPage';
import type { ChatWorkspace } from '../types';

// The client reaches lib/clientLogger, which reads `window` at import, and
// there is no jsdom here.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const NOW = new Date('2026-03-01T12:00:00Z');

const workspace = (id: number, name: string, hasToken = true): ChatWorkspace => ({
  id,
  provider: 'slack',
  externalId: `T${id}`,
  name,
  hasToken,
  createdAt: '2026-03-01T11:00:00Z',
  projectIds: [],
});

const FELLOWSHIP = workspace(1, 'Fellowship');
const noop = () => {};

describe('the workspace table', () => {
  const table = (rows: ChatWorkspace[], busy = false) =>
    renderToStaticMarkup(
      <WorkspaceTable workspaces={rows} now={NOW} busy={busy} onReplace={async () => {}} onRemove={noop} />,
    );

  it('shows each workspace by name, with the word Token set and when it was connected', () => {
    const html = table([FELLOWSHIP, workspace(2, 'Youth')]);
    expect(html).toContain('Fellowship');
    expect(html).toContain('Youth');
    expect(html.match(/Token set/g)).toHaveLength(2);
    expect(html).toContain('<th>Connected</th>');
  });

  it('never draws anything token-shaped', () => {
    const html = table([FELLOWSHIP]);
    expect(html).not.toMatch(/xoxb-/);
    expect(html).not.toMatch(/xox[a-z]-/);
  });

  it('says no token where the server holds none', () => {
    expect(table([workspace(3, 'Bare', false)])).toContain('No token');
  });

  it('has a Replace token and a Remove for every row', () => {
    const html = table([FELLOWSHIP, workspace(2, 'Youth')]);
    expect(html.match(/Replace token/g)).toHaveLength(2);
    expect(html.match(/>Remove</g)).toHaveLength(2);
  });

  it('disables the row controls while a write is in flight', () => {
    expect(table([FELLOWSHIP], true).match(/disabled=""/g)?.length).toBeGreaterThanOrEqual(2);
  });
});

describe('the replace token input', () => {
  it('is a password input that names the workspace, and starts empty', () => {
    const html = renderToStaticMarkup(<ReplaceTokenForm workspace={FELLOWSHIP} busy={false} onReplace={async () => {}} />);
    expect(html).toContain('type="password"');
    expect(html).toContain('autoComplete="off"');
    expect(html).toContain('aria-label="New token for Fellowship"');
    expect(html).not.toContain('value="xoxb');
  });
});

describe('the remove dialog', () => {
  it('draws nothing while nothing waits', () => {
    const html = renderToStaticMarkup(<RemoveDialog workspace={null} busy={false} onClose={noop} onConfirm={noop} />);
    expect(html).not.toContain('Remove');
  });

  it('names the workspace and carries the confirm and a way out', () => {
    const html = renderToStaticMarkup(<RemoveDialog workspace={FELLOWSHIP} busy={false} onClose={noop} onConfirm={noop} />);
    expect(html).toContain('Remove Fellowship?');
    expect(html).toContain('Leave it');
    expect(html).toContain('>Remove<');
  });
});

describe('the add form', () => {
  const form = (failure: string | null, token = '') =>
    renderToStaticMarkup(<AddTokenForm token={token} busy={false} failure={failure} onToken={noop} onSubmit={noop} />);

  it('takes the token in a password input with autocomplete off', () => {
    const html = form(null);
    expect(html).toContain('type="password"');
    expect(html).toContain('autoComplete="off"');
    expect(html).toContain('Connect');
  });

  it('shows the server sentence under the input when a token is refused', () => {
    const html = form('invalid_auth');
    expect(html).toContain('<p class="text-danger">invalid_auth</p>');
  });

  it('shows no failure line otherwise', () => {
    expect(form(null)).not.toContain('text-danger');
  });

  it('leaves Connect disabled until something is typed', () => {
    const button = (html: string) => html.match(/<button[^>]*>/)![0];
    expect(button(form(null))).toContain('disabled=""');
    expect(button(form(null, 'xoxb-a.b.c'))).not.toContain('disabled');
  });
});

describe('the manifest block', () => {
  it('puts the manifest text in a pre, and says where it goes', () => {
    const html = renderToStaticMarkup(<ManifestBlock manifest={'{"display_information":{"name":"Hatch"}}'} />);
    expect(html).toMatch(/<pre[^>]*class="hatch-chat-manifest"[^>]*>/);
    expect(html).toContain('&quot;display_information&quot;');
    expect(html).toContain('create an app from a manifest');
  });

  it('says it is reading while the manifest has not come', () => {
    const html = renderToStaticMarkup(<ManifestBlock manifest={null} />);
    expect(html).toContain('Reading the manifest');
    expect(html).not.toContain('<pre');
  });
});
