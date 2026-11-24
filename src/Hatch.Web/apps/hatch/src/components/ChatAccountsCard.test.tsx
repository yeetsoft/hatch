import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { AccountRow, ChatAccountsCard, MemberPicker, SuggestionPrompt, UnlinkDialog } from './ChatAccountsCard';
import type { ConfirmOutcome } from '../lib/chatLinks';
import type { MeChat, MeChatLink, MeChatMember, MeChatWorkspace } from '../types';

// The client reaches lib/clientLogger, which reads `window` at import, and
// there is no jsdom here.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const NOW = Date.parse('2026-06-01T12:00:00Z');
const noop = () => {};
const waiting: MeChatLink = {
  id: 2,
  state: 'waiting',
  memberName: 'Bo Tan',
  expiresAt: '2026-06-01T13:00:00Z',
  runnerFailures: true,
  lastRefusal: null,
  lastRefusalAt: null,
};
const linked: MeChatLink = {
  id: 1,
  state: 'linked',
  memberName: 'Ada Reed',
  expiresAt: null,
  runnerFailures: true,
  lastRefusal: null,
  lastRefusalAt: null,
};
const member = (id: string, name: string, displayName: string | null = null): MeChatMember => ({
  id,
  name,
  displayName,
});

const draw = (chat: MeChat | null, notice: ConfirmOutcome | null = null): string =>
  renderToStaticMarkup(<ChatAccountsCard chat={chat} notice={notice} now={NOW} onChanged={noop} />);

const acme = (link: MeChatLink | null): MeChatWorkspace => ({ id: 1, name: 'Acme', link });
const globex = (link: MeChatLink | null): MeChatWorkspace => ({ id: 2, name: 'Globex', link });

describe('ChatAccountsCard', () => {
  it('draws nothing before the list is read', () => {
    expect(draw(null)).toBe('');
  });

  it('draws nothing with no workspace', () => {
    expect(draw({ linksOn: true, workspaces: [] })).toBe('');
  });

  it('draws one row per workspace under a Slack accounts heading', () => {
    const html = draw({ linksOn: true, workspaces: [acme(linked), globex(null)] });
    expect(html).toContain('Slack accounts');
    expect(html).toContain('Acme');
    expect(html).toContain('Globex');
    expect(html).toContain('Linked as Ada Reed');
    expect(html).toContain('Not linked');
  });

  it('offers Link where nothing is linked and Unlink where something is', () => {
    expect(draw({ linksOn: true, workspaces: [globex(null)] })).toContain('>Link<');
    expect(draw({ linksOn: true, workspaces: [globex(null)] })).not.toContain('>Unlink<');
    const html = draw({ linksOn: true, workspaces: [acme(linked)] });
    expect(html).toContain('>Unlink<');
    expect(html).not.toContain('>Link<');
  });

  it('says whose member a waiting message went to, and offers Link again', () => {
    const html = draw({ linksOn: true, workspaces: [globex(waiting)] });
    expect(html).toContain('Bo Tan');
    expect(html).toContain('Waiting');
    expect(html).toContain('>Link<');
  });

  it('reads an expired waiting link as ran out', () => {
    const html = draw({
      linksOn: true,
      workspaces: [globex({ ...waiting, expiresAt: '2026-06-01T11:00:00Z' })],
    });
    expect(html).toContain('ran out');
    expect(html).toContain('>Link<');
  });

  it('names HATCH_PUBLIC_URL and draws no Link button when links are off', () => {
    const html = draw({ linksOn: false, workspaces: [globex(null)] });
    expect(html).toContain('HATCH_PUBLIC_URL');
    expect(html).not.toContain('>Link<');
  });

  it('keeps Unlink when links are off, so a stale link can still be removed', () => {
    expect(draw({ linksOn: false, workspaces: [acme(linked)] })).toContain('>Unlink<');
  });

  it('draws the confirm notice above the section, and alone when there is no section', () => {
    const notice = { words: 'Linked as Ada Reed.', failed: false };
    const html = draw({ linksOn: true, workspaces: [acme(linked)] }, notice);
    expect(html.indexOf('Linked as Ada Reed.')).toBeLessThan(html.indexOf('Slack accounts'));
    expect(draw({ linksOn: true, workspaces: [] }, notice)).toContain('Linked as Ada Reed.');
  });

  it('draws a failed notice as an error', () => {
    const html = draw(null, { words: 'That link was already used.', failed: true });
    expect(html).toContain('text-danger');
  });

  it('draws no picker or message until Link is pressed', () => {
    const html = renderToStaticMarkup(
      <AccountRow
        workspace={globex(null)}
        linksOn
        now={NOW}
        open={false}
        runnerFailuresChecked={false}
        runnerFailuresSaving={false}
        onSwitchRunnerFailures={noop}
        onLink={noop}
        onClose={noop}
        onUnlink={noop}
        onLinked={noop}
      />,
    );
    expect(html).not.toContain('Find yourself');
  });

  it('draws the Runner failures switch only on a linked row', () => {
    const html = draw({ linksOn: true, workspaces: [acme(linked), globex(waiting)] });
    expect(html).toContain('Runner failures');
    expect(html).toContain('Direct messages when one of your runners cannot work.');
    const globexHtml = draw({ linksOn: true, workspaces: [globex(waiting)] });
    expect(globexHtml).not.toContain('Runner failures');
  });

  it('draws no switch on an empty row', () => {
    expect(draw({ linksOn: true, workspaces: [globex(null)] })).not.toContain('Runner failures');
  });

  it('checks the switch when runnerFailures is true, and leaves it unchecked when false', () => {
    const on = draw({ linksOn: true, workspaces: [acme(linked)] });
    expect(on).toContain('checked=""');
    const off = draw({ linksOn: true, workspaces: [acme({ ...linked, runnerFailures: false })] });
    expect(off).not.toContain('checked=""');
  });

  it('draws the refusal line with its sentence and when, and hides it once a send works', () => {
    const refused = { ...linked, lastRefusal: 'channel_not_found', lastRefusalAt: '2026-06-01T12:00:00Z' };
    expect(draw({ linksOn: true, workspaces: [acme(refused)] })).toContain('Slack could not send to this account');
    expect(draw({ linksOn: true, workspaces: [acme(linked)] })).not.toContain('Slack could not send to this account');
  });
});

describe('SuggestionPrompt', () => {
  it('offers the member by name with a button to link as them and another to pick someone else', () => {
    const html = renderToStaticMarkup(
      <SuggestionPrompt workspaceName="Acme" member={member('U1', 'Ada Reed', 'ada')} busy={false} onAccept={noop} onOther={noop} />,
    );
    expect(html).toContain('Ada Reed');
    expect(html).toContain('Link as Ada Reed');
    expect(html).toContain('Pick someone else');
  });
});

describe('MemberPicker', () => {
  const members = [member('U1', 'Ada Reed', 'ada'), member('U2', 'Bo Tan', 'Bobby'), member('U3', 'Cy Lo')];

  it('says it is reading until the list arrives', () => {
    expect(renderToStaticMarkup(<MemberPicker members={null} busy={false} onChoose={noop} />)).toContain('Reading');
  });

  it('lists name and display name only, never an id or email', () => {
    const html = renderToStaticMarkup(<MemberPicker members={members} busy={false} onChoose={noop} />);
    expect(html).toContain('Ada Reed');
    expect(html).toContain('Bobby');
    expect(html).not.toContain('U1');
    expect(html).not.toContain('@');
  });

  it('filters what it lists by the typed query, case-blind', () => {
    const html = renderToStaticMarkup(<MemberPicker members={members} busy={false} initialQuery="BOB" onChoose={noop} />);
    expect(html).toContain('Bo Tan');
    expect(html).not.toContain('Ada Reed');
  });

  it('says so when nobody matches', () => {
    const html = renderToStaticMarkup(<MemberPicker members={members} busy={false} initialQuery="zzz" onChoose={noop} />);
    expect(html).toContain('Nobody matches');
  });
});

describe('UnlinkDialog', () => {
  it('draws nothing while no link is chosen', () => {
    expect(renderToStaticMarkup(<UnlinkDialog target={null} busy={false} onClose={noop} onConfirm={noop} />)).not.toContain(
      'Leave it',
    );
  });

  it('asks first, with a way to leave it and a danger button', () => {
    const html = renderToStaticMarkup(
      <UnlinkDialog target={{ workspace: acme(linked), link: linked }} busy={false} onClose={noop} onConfirm={noop} />,
    );
    expect(html).toContain('Unlink Acme?');
    expect(html).toContain('Leave it');
    expect(html).toContain('hatch-btn--danger');
  });
});
