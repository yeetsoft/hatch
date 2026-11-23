import { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Modal, Text } from '@hatch/ui';
import {
  createChatRoom,
  deleteChatRoom,
  getChatChannels,
  getChatRooms,
  testChatRoom,
  updateChatRoom,
} from '../api/client';
import { message } from '../lib/errors';
import type { ChatChannel, ChatRoom, ChatRoomPatch, ChatWorkspace, Project } from '../types';

/** What a room's Send a test message button has to say: still waiting, or the
    line to show. `text` is "Sent" or the refusal's sentence. */
export interface RoomTest {
  waiting: boolean;
  text: string | null;
}

/** The carries-none sentence. Chosen projects with none ticked posts nothing,
    and says so in these words rather than reading as every project. */
export const CARRIES_NONE = 'This room carries no project.';

/** Where the channel list stands: asked for, refused, or read. */
export type ChannelState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'channels'; channels: ChatChannel[] };

/**
 * The rooms one workspace posts to, and the way to add one.
 *
 * It re-reads the rooms whenever the workspace's bound set changes: unbinding a
 * project takes it from a room's chosen list on the server, and the page asks
 * the server what is left rather than guessing.
 */
export function ChatRooms({ workspace, projects }: { workspace: ChatWorkspace; projects: Project[] }) {
  const [rooms, setRooms] = useState<ChatRoom[] | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [busyRooms, setBusyRooms] = useState<number[]>([]);
  const [roomFailures, setRoomFailures] = useState<Record<number, string>>({});
  const [tests, setTests] = useState<Record<number, RoomTest>>({});
  const [picker, setPicker] = useState<ChannelState | null>(null);
  const [pickFailure, setPickFailure] = useState<string | null>(null);
  const [pending, setPending] = useState<ChatRoom | null>(null);
  const [removing, setRemoving] = useState(false);

  const bound = workspace.projectIds.join(',');
  const boundProjects = projects.filter((p) => workspace.projectIds.includes(p.id));

  // Only the newest read is kept: a slow answer to an older one must not
  // overwrite what a later read already found.
  const latest = useRef(0);

  const load = useCallback(async () => {
    const mine = ++latest.current;
    try {
      const read = await getChatRooms(workspace.id);
      if (mine === latest.current) {
        setRooms(read);
        setFailure(null);
      }
    } catch (err) {
      if (mine === latest.current) setFailure(message(err));
    }
  }, [workspace.id]);

  // `bound` is read only to re-run the effect when the set changes.
  useEffect(() => {
    void load();
  }, [load, bound]);

  function working(id: number, on: boolean) {
    setBusyRooms((ids) => (on ? [...ids, id] : ids.filter((x) => x !== id)));
  }

  async function patch(room: ChatRoom, change: ChatRoomPatch) {
    working(room.id, true);
    try {
      const updated = await updateChatRoom(room.id, change);
      setRooms((list) => list?.map((r) => (r.id === updated.id ? updated : r)) ?? list);
      setRoomFailures((f) => ({ ...f, [room.id]: '' }));
    } catch (err) {
      setRoomFailures((f) => ({ ...f, [room.id]: message(err) }));
    } finally {
      working(room.id, false);
    }
  }

  async function test(room: ChatRoom) {
    setTests((t) => ({ ...t, [room.id]: { waiting: true, text: null } }));
    try {
      const result = await testChatRoom(room.id);
      const text = result.delivered ? 'Sent' : (result.reason ?? 'Slack did not take the message.');
      setTests((t) => ({ ...t, [room.id]: { waiting: false, text } }));
    } catch (err) {
      setTests((t) => ({ ...t, [room.id]: { waiting: false, text: message(err) } }));
    }
  }

  async function openPicker() {
    setPicker({ kind: 'loading' });
    setPickFailure(null);
    try {
      setPicker({ kind: 'channels', channels: await getChatChannels(workspace.id) });
    } catch (err) {
      setPicker({ kind: 'error', message: message(err) });
    }
  }

  async function pick(channel: ChatChannel) {
    setPickFailure(null);
    try {
      await createChatRoom(workspace.id, {
        channelId: channel.id,
        channelName: channel.name,
        changelog: false,
        questions: false,
        problems: false,
        unownedProblems: false,
        allProjects: true,
      });
    } catch (err) {
      setPickFailure(message(err));
    } finally {
      await load();
    }
  }

  async function remove(room: ChatRoom) {
    setRemoving(true);
    try {
      await deleteChatRoom(room.id);
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    } finally {
      setRemoving(false);
      setPending(null);
      await load();
    }
  }

  return (
    <div className="hatch-chat-rooms" role="group" aria-label={`Rooms on ${workspace.name}`}>
      {failure && <p className="text-danger">{failure}</p>}
      {rooms === null && !failure && <Text tone="muted">Reading the rooms…</Text>}
      {rooms?.length === 0 && <Text tone="muted">No room is set up on {workspace.name}.</Text>}
      {rooms?.map((room) => (
        <RoomCard
          key={room.id}
          room={room}
          boundProjects={boundProjects}
          busy={busyRooms.includes(room.id)}
          failure={roomFailures[room.id] || null}
          test={tests[room.id]}
          onPatch={(change) => void patch(room, change)}
          onTest={() => void test(room)}
          onRemove={() => setPending(room)}
        />
      ))}

      <div className="hatch-form-actions">
        <Button onClick={() => void openPicker()} disabled={picker?.kind === 'loading'}>
          Add a room
        </Button>
        {picker && <Button onClick={() => setPicker(null)}>Close</Button>}
      </div>
      {picker && (
        <>
          <ChannelPicker state={picker} rooms={rooms ?? []} onPick={(c) => void pick(c)} />
          {pickFailure && <p className="text-danger">{pickFailure}</p>}
        </>
      )}

      <RemoveRoomDialog room={pending} busy={removing} onClose={() => setPending(null)} onConfirm={(r) => void remove(r)} />
    </div>
  );
}

const SWITCHES = [
  ['changelog', 'Changelog'],
  ['questions', 'Questions'],
  ['problems', 'Problems'],
  ['unownedProblems', 'Problems that belong to no project'],
] as const;

/**
 * One room: the channel, four switches, which projects it carries, and the
 * test and remove buttons. Everything arrives as props, so it renders without
 * a network.
 */
export function RoomCard({
  room,
  boundProjects,
  busy,
  failure,
  test,
  onPatch,
  onTest,
  onRemove,
}: {
  room: ChatRoom;
  /** This workspace's bound projects, and only those: a room can choose from nothing else. */
  boundProjects: Project[];
  busy: boolean;
  failure?: string | null;
  test?: RoomTest;
  onPatch: (change: ChatRoomPatch) => void;
  onTest: () => void;
  onRemove: () => void;
}) {
  const carriesNone = !room.allProjects && room.projectIds.length === 0;
  const waiting = test?.waiting ?? false;

  function tick(project: Project, on: boolean) {
    const next = on ? [...room.projectIds, project.id] : room.projectIds.filter((id) => id !== project.id);
    onPatch({ projectIds: next });
  }

  return (
    <section className="hatch-chat-room" aria-label={`Room ${room.channelName}`}>
      <h3 className="hatch-chat-room__name">#{room.channelName}</h3>

      <div role="group" aria-label="What it posts" className="hatch-chat-room__switches">
        {SWITCHES.map(([field, label]) => (
          <label key={field} className="hatch-chat-room__switch">
            <input
              type="checkbox"
              checked={room[field]}
              disabled={busy}
              onChange={(e) => onPatch({ [field]: e.target.checked })}
            />
            {label}
          </label>
        ))}
      </div>

      <div role="radiogroup" aria-label="Which projects" className="hatch-chat-room__scope">
        <label className="hatch-chat-room__switch">
          <input
            type="radio"
            name={`room-${room.id}-scope`}
            checked={room.allProjects}
            disabled={busy}
            onChange={() => onPatch({ allProjects: true })}
          />
          Every bound project
        </label>
        <label className="hatch-chat-room__switch">
          <input
            type="radio"
            name={`room-${room.id}-scope`}
            checked={!room.allProjects}
            disabled={busy}
            onChange={() => onPatch({ allProjects: false })}
          />
          Chosen projects
        </label>
      </div>

      {!room.allProjects && (
        <div role="group" aria-label={`Projects ${room.channelName} carries`} className="hatch-chat-room__chosen">
          {boundProjects.map((project) => (
            <label key={project.id} className="hatch-chat-room__switch">
              <input
                type="checkbox"
                checked={room.projectIds.includes(project.id)}
                disabled={busy}
                onChange={(e) => tick(project, e.target.checked)}
              />
              <span className="hatch-chat-projects__key">{project.key}</span> {project.name}
            </label>
          ))}
        </div>
      )}
      {carriesNone && <p className="hatch-chat-room__none">{CARRIES_NONE}</p>}

      {failure && <p className="text-danger">{failure}</p>}

      <div className="hatch-chat-room__actions">
        <Button loading={waiting} disabled={waiting} onClick={onTest}>
          Send a test message
        </Button>
        <Button variant="danger" disabled={busy} onClick={onRemove}>
          Remove room
        </Button>
      </div>
      {test?.text && (
        <p role="status" className="hatch-chat-room__result">
          {test.text}
        </p>
      )}
    </section>
  );
}

/** The channels the token can see, each with whether it can be added. */
export function ChannelPicker({
  state,
  rooms,
  onPick,
}: {
  state: ChannelState;
  rooms: ChatRoom[];
  onPick: (channel: ChatChannel) => void;
}) {
  if (state.kind === 'loading') return <Text tone="muted">Reading the channels…</Text>;
  if (state.kind === 'error') return <p className="text-danger">{state.message}</p>;
  if (state.channels.length === 0) return <Text tone="muted">The app can see no channel.</Text>;

  const taken = new Set(rooms.map((r) => r.channelId));

  return (
    <ul className="hatch-chat-picker" aria-label="Channels">
      {state.channels.map((channel) => {
        const already = taken.has(channel.id);
        return (
          <li key={channel.id} className="hatch-chat-channel">
            <span className="hatch-chat-channel__name">
              <strong>#{channel.name}</strong>
              {channel.isPrivate && <span className="hatch-chat-channel__mark"> Private</span>}
            </span>
            <span className="hatch-chat-channel__note">
              {already ? 'Already a room' : !channel.isMember ? 'Not invited yet. In Slack, open the channel and add Hatch.' : ''}
            </span>
            <Button disabled={already} aria-label={`Add ${channel.name}`} onClick={() => onPick(channel)}>
              Add
            </Button>
          </li>
        );
      })}
    </ul>
  );
}

/** The question Remove room waits on. Only its confirm button deletes. */
export function RemoveRoomDialog({
  room,
  busy,
  onClose,
  onConfirm,
}: {
  room: ChatRoom | null;
  busy: boolean;
  onClose: () => void;
  onConfirm: (room: ChatRoom) => void;
}) {
  return (
    <Modal open={room !== null} onClose={onClose} title={`Remove room #${room?.channelName ?? ''}?`}>
      {room && (
        <div className="hatch-claim-clear">
          <p>
            Hatch stops posting to #{room.channelName}. Nothing in the channel itself is touched, and it can be set up as a
            room again.
          </p>
          <div className="hatch-form-actions">
            <Button onClick={onClose}>Leave it</Button>
            <Button variant="danger" loading={busy} onClick={() => onConfirm(room)}>
              Remove room
            </Button>
          </div>
        </div>
      )}
    </Modal>
  );
}
