import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { AddTokenForm, AppTokenCell, DeliveryState, LinksOffNote, ListenState, ManifestBlock, RemoveDialog, ReplaceTokenForm, WorkspaceTable } from './ChatPage';
import type { ChatWorkspace, ChatWorkspaceDelivery, Project } from '../types';

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
  hasAppToken: false,
  appTokenUnreadable: false,
  createdAt: '2026-03-01T11:00:00Z',
  projectIds: [],
});

const project = (id: number, key: string, name: string): Project => ({ id, key, name }) as Project;
const PROJECTS = [project(1, 'AER', 'Aerie'), project(2, 'BEE', 'Hive')];

const FELLOWSHIP = workspace(1, 'Fellowship');
const noop = () => {};

const table = (rows: ChatWorkspace[], busy = false, delivery: ChatWorkspaceDelivery[] = []) =>
  renderToStaticMarkup(
    <WorkspaceTable
      workspaces={rows}
      projects={PROJECTS}
      delivery={delivery}
      now={NOW}
      busy={busy}
      onBound={noop}
      onReplace={async () => {}}
      onSetAppToken={async () => {}}
      onRemoveAppToken={noop}
      onRemove={noop}
    />,
  );

const withAppToken = (over: Partial<ChatWorkspace>): ChatWorkspace => ({ ...workspace(1, 'Fellowship'), ...over });
const cell = (w: ChatWorkspace, busy = false) =>
  renderToStaticMarkup(<AppTokenCell workspace={w} busy={busy} onSet={async () => {}} onRemove={noop} />);

describe('the workspace table', () => {
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

  it('has a Projects column that lists every project to bind, ticked where bound', () => {
    const html = table([{ ...FELLOWSHIP, projectIds: [2] }]);
    expect(html).toContain('<th>Projects</th>');
    expect(html).toContain('aria-label="Projects bound to Fellowship"');
    expect(html).toContain('Aerie');
    expect(html).toContain('Hive');
    expect(html.match(/type="checkbox"/g)).toHaveLength(2);
    expect(html.match(/checked=""/g)).toHaveLength(1);
  });

  it('disables the row controls while a write is in flight', () => {
    expect(table([FELLOWSHIP], true).match(/disabled=""/g)?.length).toBeGreaterThanOrEqual(2);
  });
});

describe('the app token cell', () => {
  const SET = withAppToken({ hasAppToken: true });
  const UNREADABLE = withAppToken({ hasAppToken: true, appTokenUnreadable: true });

  it('is a column of the table, in its own cell', () => {
    const html = table([SET]);
    expect(html).toContain('<th>App token</th>');
    expect(html).toContain('class="hatch-chat-apptoken-cell"');
    expect(html.match(/<td[ >]/g)).toHaveLength(7);
  });

  it('says App token set where one is held', () => {
    const html = cell(SET);
    expect(html).toContain('App token set');
    expect(html).not.toContain('No app token');
  });

  it('says No app token where none is held', () => {
    const html = cell(withAppToken({}));
    expect(html).toContain('No app token');
    expect(html).not.toContain('App token set');
  });

  it('says the token cannot be read, and to paste it again, when it is unreadable', () => {
    const html = cell(UNREADABLE);
    expect(html).toContain('cannot be read');
    expect(html).toContain('Paste it again');
    expect(html).toContain('text-danger');
    expect(html).not.toContain('App token set');
  });

  it('offers Set app token where none is held and Replace app token where one is', () => {
    expect(cell(withAppToken({}))).toContain('Set app token');
    expect(cell(withAppToken({}))).not.toContain('Replace app token');
    expect(cell(SET)).toContain('Replace app token');
    expect(cell(SET)).not.toContain('Set app token');
  });

  it('offers Remove app token only where a token is set or unreadable', () => {
    expect(cell(withAppToken({}))).not.toContain('Remove app token');
    expect(cell(SET)).toContain('Remove app token');
    expect(cell(withAppToken({ appTokenUnreadable: true }))).toContain('Remove app token');
  });

  it('is a password box with autocomplete off, named for its workspace', () => {
    const html = cell(SET);
    expect(html).toContain('type="password"');
    expect(html).toContain('autoComplete="off"');
    expect(html).toContain('aria-label="App-level token for Fellowship"');
    expect(html).not.toMatch(/value="xapp/);
  });

  it('never draws anything shaped like an app-level token', () => {
    for (const w of [withAppToken({}), SET, UNREADABLE]) expect(cell(w)).not.toContain('xapp-');
    expect(table([SET, UNREADABLE])).not.toContain('xapp-');
  });

  it('disables its buttons while a write is in flight, and the Set button while the box is blank', () => {
    expect(cell(SET).match(/disabled=""/g)).toHaveLength(1);
    expect(cell(SET, true).match(/disabled=""/g)).toHaveLength(2);
    expect(cell(withAppToken({}), true).match(/disabled=""/g)).toHaveLength(1);
  });
});

const delivery = (over: Partial<ChatWorkspaceDelivery> = {}): ChatWorkspaceDelivery => ({
  workspaceId: 1,
  waiting: 0,
  dropped: 0,
  lastSaid: null,
  lastSaidAt: null,
  failingSince: null,
  listeningSince: null,
  listenFailingSince: null,
  listenLastSaid: null,
  ...over,
});

describe('what is waiting to be sent', () => {
  const state = (over: Partial<ChatWorkspaceDelivery>) =>
    renderToStaticMarkup(<DeliveryState state={delivery(over)} now={NOW} />);

  it('shows how many wait, how many were dropped, and what Slack last said with its time', () => {
    const html = state({ waiting: 2, dropped: 1, lastSaid: 'ratelimited', lastSaidAt: '2026-03-01T11:57:00Z' });
    expect(html).toContain('2 waiting');
    expect(html).toContain('1 dropped');
    expect(html).toContain('Slack said: ratelimited');
    expect(html).toContain('3 minutes ago');
  });

  it('draws nothing for a workspace with none of the three', () => {
    expect(state({})).toBe('');
    expect(renderToStaticMarkup(<DeliveryState state={undefined} now={NOW} />)).toBe('');
  });

  it('leaves out the numbers that are zero', () => {
    const html = state({ waiting: 3 });
    expect(html).toContain('3 waiting');
    expect(html).not.toContain('dropped');
    expect(html).not.toContain('Slack said');
  });

  it('is a cell in the workspace row, not a row of its own', () => {
    const html = table([FELLOWSHIP, workspace(2, 'Youth')], false, [delivery({ waiting: 4 })]);
    expect(html).toContain('<th>Sending</th>');
    expect(html.match(/<tr>/g)).toHaveLength(3);
    expect(html).toContain('4 waiting');
    expect(html.match(/Token set/g)).toHaveLength(2);
  });

  it('says messages carry no link, and names HATCH_PUBLIC_URL', () => {
    const html = renderToStaticMarkup(<LinksOffNote />);
    expect(html).toContain('no link');
    expect(html).toContain('HATCH_PUBLIC_URL');
  });
});

describe('whether Hatch is listening', () => {
  const HELD = withAppToken({ hasAppToken: true });
  const UNREADABLE = withAppToken({ hasAppToken: true, appTokenUnreadable: true });
  const listen = (w: ChatWorkspace, over?: Partial<ChatWorkspaceDelivery>) =>
    renderToStaticMarkup(<ListenState workspace={w} state={over ? delivery(over) : undefined} now={NOW} />);

  it('draws nothing where no app token is held', () => {
    expect(listen(withAppToken({}))).toBe('');
    expect(listen(withAppToken({}), { listeningSince: '2026-03-01T11:00:00Z' })).toBe('');
  });

  it('says Listening, and when the connection opened', () => {
    const html = listen(HELD, { listeningSince: '2026-03-01T11:57:00Z' });
    expect(html).toContain('Listening');
    expect(html).toContain('opened 3 minutes ago');
    expect(html).not.toContain('Not listening');
    expect(html).not.toContain('text-danger');
  });

  it('says Not listening in red, with when it first failed and what Slack said', () => {
    const html = listen(HELD, {
      listeningSince: '2026-03-01T08:00:00Z',
      listenFailingSince: '2026-03-01T11:55:00Z',
      listenLastSaid: 'invalid_auth',
    });
    expect(html).toContain('text-danger');
    expect(html).toContain('Not listening');
    expect(html).not.toContain('Not listening yet');
    expect(html).toContain('first failed 5 minutes ago');
    expect(html).toContain('Slack said: invalid_auth');
    expect(html).not.toContain('opened');
  });

  it('says Not listening yet, not in red, for a held token that has neither opened nor failed', () => {
    const html = listen(HELD, {});
    expect(html).toContain('Not listening yet');
    expect(html).not.toContain('text-danger');
    expect(html).not.toContain('Slack said');
  });

  it('reads Not listening for an unreadable token with a stale open time, and does not give Slack the words', () => {
    const html = listen(UNREADABLE, {
      listeningSince: '2026-03-01T08:00:00Z',
      listenLastSaid: 'The app token cannot be read.',
    });
    expect(html).toContain('Not listening');
    expect(html).toContain('text-danger');
    expect(html).not.toContain('Listening<');
    expect(html).not.toContain('Slack said');
  });

  it('reads Not listening yet for a held token with no delivery entry', () => {
    const html = listen(HELD);
    expect(html).toContain('Not listening yet');
  });

  it('is drawn inside the app token cell of the table', () => {
    const html = table([HELD], false, [delivery({ listeningSince: '2026-03-01T11:57:00Z' })]);
    const cellHtml = html.slice(html.indexOf('hatch-chat-apptoken-cell'), html.indexOf('hatch-chat-projects-cell'));
    expect(cellHtml).toContain('hatch-chat-listen');
    expect(cellHtml).toContain('opened 3 minutes ago');
    expect(html.match(/<td[ >]/g)).toHaveLength(7);
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
