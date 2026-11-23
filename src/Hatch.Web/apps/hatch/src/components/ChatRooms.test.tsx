import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { CARRIES_NONE, ChannelPicker, RemoveRoomDialog, RoomCard, type RoomTest } from './ChatRooms';
import type { ChatChannel, ChatRoom, Project } from '../types';

// The client reaches lib/clientLogger, which reads `window` at import, and
// there is no jsdom here.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const project = (id: number, key: string, name: string): Project => ({ id, key, name }) as Project;
const BOUND = [project(1, 'AER', 'Aerie'), project(2, 'BEE', 'Hive')];

const room = (over: Partial<ChatRoom> = {}): ChatRoom => ({
  id: 5,
  workspaceId: 7,
  channelId: 'C1',
  channelName: 'general',
  changelog: false,
  questions: false,
  problems: false,
  unownedProblems: false,
  allProjects: true,
  projectIds: [],
  ...over,
});

const noop = () => {};

const card = (r: ChatRoom, extra: { busy?: boolean; test?: RoomTest; failure?: string } = {}) =>
  renderToStaticMarkup(
    <RoomCard
      room={r}
      boundProjects={BOUND}
      busy={extra.busy ?? false}
      test={extra.test}
      failure={extra.failure}
      onPatch={noop}
      onTest={noop}
      onRemove={noop}
    />,
  );

describe('a room card', () => {
  it('names the channel and draws the four switches and the two choices', () => {
    const html = card(room());
    expect(html).toContain('#general');
    for (const label of ['Changelog', 'Questions', 'Problems', 'Problems that belong to no project']) {
      expect(html).toContain(label);
    }
    expect(html).toContain('Every bound project');
    expect(html).toContain('Chosen projects');
  });

  it('shows the switches as they stand', () => {
    const html = card(room({ changelog: true, problems: true }));
    // two switches and the all-projects radio
    expect(html.match(/checked=""/g)).toHaveLength(3);
  });

  it('offers no project list while the room carries every bound project', () => {
    const html = card(room());
    expect(html).not.toContain('Aerie');
    expect(html).not.toContain(CARRIES_NONE);
  });

  it('offers only this workspace\'s bound projects once chosen, ticking the chosen ones', () => {
    const html = card(room({ allProjects: false, projectIds: [2] }));
    expect(html).toContain('Aerie');
    expect(html).toContain('Hive');
    // the Chosen radio and the one ticked project
    expect(html.match(/checked=""/g)).toHaveLength(2);
    expect(html).not.toContain(CARRIES_NONE);
  });

  it('says exactly "This room carries no project." for chosen projects with none left', () => {
    const html = card(room({ allProjects: false, projectIds: [] }));
    expect(html).toContain('This room carries no project.');
  });

  it('never says every project outside the radio label', () => {
    const html = card(room({ allProjects: false, projectIds: [] }));
    expect(html.replaceAll('Every bound project', '')).not.toMatch(/every project/i);
    expect(html.replaceAll('Every bound project', '')).not.toMatch(/every bound project/i);
  });

  it('says the same where chosen is picked and nothing is bound', () => {
    const html = renderToStaticMarkup(
      <RoomCard
        room={room({ allProjects: false })}
        boundProjects={[]}
        busy={false}
        onPatch={noop}
        onTest={noop}
        onRemove={noop}
      />,
    );
    expect(html).toContain(CARRIES_NONE);
  });

  it('has a test button and a Remove room button, and no result before a test', () => {
    const html = card(room());
    expect(html).toContain('Send a test message');
    expect(html).toContain('Remove room');
    expect(html).not.toContain('role="status"');
  });

  it('says Sent in a status paragraph after a delivered test', () => {
    const html = card(room(), { test: { waiting: false, text: 'Sent' } });
    expect(html).toContain('<p role="status" class="hatch-chat-room__result">Sent</p>');
  });

  it('shows a refusal\'s sentence the same way', () => {
    const sentence = 'the app has not been invited to that channel; in Slack, add Hatch to it';
    const html = card(room(), { test: { waiting: false, text: sentence } });
    expect(html).toContain(`role="status"`);
    expect(html).toContain(sentence);
  });

  it('disables the test button, and marks it busy, while it waits', () => {
    const html = card(room(), { test: { waiting: true, text: null } });
    expect(html).toMatch(/aria-busy="true"[^>]*disabled=""[^>]*>Send a test message/);
  });

  it('disables the switches and Remove room while a change is in flight', () => {
    const html = card(room(), { busy: true });
    // four switches, two radios, Remove room
    expect(html.match(/disabled=""/g)).toHaveLength(7);
  });

  it('shows a failed change under the card', () => {
    expect(card(room(), { failure: 'that project is not bound' })).toContain('that project is not bound');
  });
});

describe('the channel picker', () => {
  const channels: ChatChannel[] = [
    { id: 'C1', name: 'general', isPrivate: false, isMember: true },
    { id: 'C2', name: 'announcements', isPrivate: false, isMember: false },
    { id: 'C3', name: 'leaders', isPrivate: true, isMember: true },
  ];
  const picker = (rooms: ChatRoom[] = []) =>
    renderToStaticMarkup(<ChannelPicker state={{ kind: 'channels', channels }} rooms={rooms} onPick={noop} />);

  it('lists every channel by name', () => {
    const html = picker();
    for (const name of ['#general', '#announcements', '#leaders']) expect(html).toContain(name);
  });

  it('gives a channel the app is in a plain row', () => {
    const html = picker();
    expect(html.match(/Not invited yet/g)).toHaveLength(1);
    expect(html).not.toContain('Already a room');
  });

  it('tells a non-member channel to be invited in Slack', () => {
    expect(picker()).toContain('Not invited yet. In Slack, open the channel and add Hatch.');
  });

  it('marks a private channel', () => {
    expect(picker().match(/Private/g)).toHaveLength(1);
  });

  it('marks a channel that is already a room, and disables its button', () => {
    const html = picker([room({ channelId: 'C1' })]);
    expect(html).toContain('Already a room');
    expect(html.match(/disabled=""/g)).toHaveLength(1);
    expect(html).toMatch(/aria-label="Add general"[^>]*disabled=""|disabled=""[^>]*aria-label="Add general"/);
  });

  it('says it is reading while the channels load', () => {
    const html = renderToStaticMarkup(<ChannelPicker state={{ kind: 'loading' }} rooms={[]} onPick={noop} />);
    expect(html).toContain('Reading the channels');
    expect(html).not.toContain('<ul');
  });

  it('shows the sentence on a refusal, and no list', () => {
    const html = renderToStaticMarkup(
      <ChannelPicker state={{ kind: 'error', message: 'Slack could not be reached; try again' }} rooms={[]} onPick={noop} />,
    );
    expect(html).toContain('Slack could not be reached; try again');
    expect(html).toContain('text-danger');
    expect(html).not.toContain('<ul');
  });
});

describe('the remove room dialog', () => {
  it('draws nothing while nothing waits', () => {
    const html = renderToStaticMarkup(<RemoveRoomDialog room={null} busy={false} onClose={noop} onConfirm={noop} />);
    expect(html).not.toContain('Remove room');
  });

  it('names the channel and carries the confirm and a way out', () => {
    const html = renderToStaticMarkup(<RemoveRoomDialog room={room()} busy={false} onClose={noop} onConfirm={noop} />);
    expect(html).toContain('Remove room #general?');
    expect(html).toContain('Leave it');
    expect(html).toContain('>Remove room<');
  });
});
