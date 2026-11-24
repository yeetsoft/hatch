import { type FormEvent, useCallback, useEffect, useState } from 'react';
import { Button, Card, EmptyState, Field, Modal, PageHeader, Table, Text } from '@hatch/ui';
import {
  createChatWorkspace,
  deleteChatWorkspace,
  getChatDelivery,
  getChatManifest,
  getChatWorkspaces,
  removeChatAppToken,
  replaceChatToken,
  setChatAppToken,
} from '../api/client';
import { ChatBindings } from '../components/ChatBindings';
import { ChatRooms } from '../components/ChatRooms';
import { CopyMarkdownButton } from '../components/CopyMarkdownButton';
import { agoPhrase } from '../lib/claim';
import { message } from '../lib/errors';
import { POLL_MS } from '../lib/refresh';
import { useLoaded } from '../lib/useLoaded';
import { useProjects } from '../lib/useProjects';
import type { ChatDelivery, ChatWorkspace, ChatWorkspaceDelivery, Project } from '../types';

/**
 * An Admin's view of the chat workspaces Hatch is connected to: connect one by
 * pasting its bot token, replace a token, remove a workspace, and copy the app
 * manifest the workspace's app is created from.
 *
 * The server checks every token with the chat service and answers with the
 * workspace's own name, so this page asks for nothing but the token. A token is
 * typed into a password input, cleared after every attempt whether or not it
 * worked, and never shown again: the server sends back only whether it holds
 * one.
 */
export function ChatPage() {
  const [workspaces, setWorkspaces] = useState<ChatWorkspace[] | null>(null);
  const { projects } = useProjects();
  const [manifest, setManifest] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [addFailure, setAddFailure] = useState<string | null>(null);
  const [token, setToken] = useState('');
  const [pending, setPending] = useState<ChatWorkspace | null>(null);
  const [busy, setBusy] = useState(false);
  // What is waiting and what Slack last said move every few seconds, so they
  // are their own read, kept fresh while the page is on screen.
  const { data: delivery, reload: reloadDelivery } = useLoaded<ChatDelivery>(getChatDelivery, { everyMs: POLL_MS });

  const load = useCallback(async () => {
    try {
      setWorkspaces(await getChatWorkspaces());
    } catch (err) {
      setFailure(message(err));
    }
  }, []);

  // After a write that can change what is waiting: a new workspace, a new
  // token, a removal.
  const loadAll = useCallback(async () => {
    await Promise.all([load(), reloadDelivery()]);
  }, [load, reloadDelivery]);

  useEffect(() => {
    void load();
    getChatManifest()
      .then((m) => setManifest(m.manifest))
      .catch((err) => setFailure(message(err)));
  }, [load]);

  async function connect(e: FormEvent) {
    e.preventDefault();
    if (token.trim() === '') return;
    const typed = token;
    // Cleared before the answer, so the secret is not left in the box while the
    // service is asked, and is gone whether the answer is yes or no.
    setToken('');
    setBusy(true);
    try {
      await createChatWorkspace(typed);
      setAddFailure(null);
    } catch (err) {
      setAddFailure(message(err));
    } finally {
      setBusy(false);
      await loadAll();
    }
  }

  async function replace(workspace: ChatWorkspace, typed: string) {
    setBusy(true);
    try {
      await replaceChatToken(workspace.id, typed);
    } finally {
      setBusy(false);
      await loadAll();
    }
  }

  async function setAppToken(workspace: ChatWorkspace, typed: string) {
    setBusy(true);
    try {
      await setChatAppToken(workspace.id, typed);
    } finally {
      setBusy(false);
      await loadAll();
    }
  }

  // No dialog: an app-level token costs nothing to paste again.
  async function removeAppToken(workspace: ChatWorkspace) {
    setBusy(true);
    try {
      await removeChatAppToken(workspace.id);
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    } finally {
      setBusy(false);
      await loadAll();
    }
  }

  function bound(workspace: ChatWorkspace, projectIds: number[]) {
    setWorkspaces((rows) => rows?.map((w) => (w.id === workspace.id ? { ...w, projectIds } : w)) ?? rows);
  }

  async function remove(workspace: ChatWorkspace) {
    setBusy(true);
    try {
      await deleteChatWorkspace(workspace.id);
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    } finally {
      setBusy(false);
      setPending(null);
      await loadAll();
    }
  }

  return (
    <div className="hatch-page">
      <PageHeader
        title="Chat"
        description="The chat workspaces Hatch is connected to, the projects each is bound to, and the rooms it posts to. A test message is the only thing posted from here."
      />

      <Card>
        <ManifestBlock manifest={manifest} />
      </Card>

      <Card>
        <AddTokenForm token={token} busy={busy} failure={addFailure} onToken={setToken} onSubmit={(e) => void connect(e)} />
      </Card>

      {failure && <p className="text-danger">{failure}</p>}

      {delivery && !delivery.linksOn && (
        <Card>
          <LinksOffNote />
        </Card>
      )}

      {workspaces?.length === 0 && <EmptyState message="No workspace is connected." />}

      {workspaces && workspaces.length > 0 && (
        <Card flush>
          <WorkspaceTable
            workspaces={workspaces}
            projects={projects}
            delivery={delivery?.workspaces ?? []}
            now={new Date()}
            busy={busy}
            onBound={bound}
            onReplace={replace}
            onSetAppToken={setAppToken}
            onRemoveAppToken={removeAppToken}
            onRemove={setPending}
          />
        </Card>
      )}

      {workspaces?.map((workspace) => (
        <Card key={workspace.id}>
          <h2 className="hatch-chat-heading">Rooms on {workspace.name}</h2>
          <ChatRooms workspace={workspace} projects={projects} />
        </Card>
      ))}

      <RemoveDialog workspace={pending} busy={busy} onClose={() => setPending(null)} onConfirm={(w) => void remove(w)} />
    </div>
  );
}

/** The app manifest, to paste into the chat service's "create an app from a
    manifest" screen. A read-only block, so it stays selectable where the
    clipboard button is not drawn. */
export function ManifestBlock({ manifest }: { manifest: string | null }) {
  return (
    <div>
      <h2 className="hatch-chat-heading">App manifest</h2>
      <p>
        Paste this into Slack&apos;s &ldquo;create an app from a manifest&rdquo; screen. Once the app is installed to the
        workspace, copy its bot token from the app&apos;s install page and connect it below.
      </p>
      {manifest === null ? (
        <Text tone="muted">Reading the manifest…</Text>
      ) : (
        <>
          <pre className="hatch-chat-manifest" aria-label="App manifest">
            {manifest}
          </pre>
          <div className="hatch-form-actions">
            <CopyMarkdownButton markdown={manifest} label="Copy manifest" />
          </div>
        </>
      )}
    </div>
  );
}

/** The one input a workspace needs. A password input so the token is not read
    off a shoulder, with autocomplete off so the browser does not keep it. */
export function AddTokenForm({
  token,
  busy,
  failure,
  onToken,
  onSubmit,
}: {
  token: string;
  busy: boolean;
  failure: string | null;
  onToken: (token: string) => void;
  onSubmit: (e: FormEvent) => void;
}) {
  return (
    <form className="hatch-chat-add" onSubmit={onSubmit}>
      <Field label="Bot token" hint="From the app's install page. Hatch asks the service whose it is and names the workspace itself.">
        <input
          type="password"
          autoComplete="off"
          spellCheck={false}
          value={token}
          onChange={(e) => onToken(e.target.value)}
        />
      </Field>
      {failure && <p className="text-danger">{failure}</p>}
      <div className="hatch-form-actions">
        <Button type="submit" loading={busy} disabled={busy || token.trim() === ''}>
          Connect
        </Button>
      </div>
    </form>
  );
}

/** The connected workspaces. A token is never a column: only the word that says
    whether one is held. */
export function WorkspaceTable({
  workspaces,
  projects,
  delivery = [],
  now,
  busy,
  onBound,
  onReplace,
  onSetAppToken,
  onRemoveAppToken,
  onRemove,
}: {
  workspaces: ChatWorkspace[];
  projects: Project[];
  /** What waits to be sent to each workspace; a workspace with no entry shows nothing. */
  delivery?: ChatWorkspaceDelivery[];
  now: Date;
  busy: boolean;
  /** A workspace's bound set changed; handed the set the server answered with. */
  onBound: (workspace: ChatWorkspace, projectIds: number[]) => void;
  /** Rejects with the service's sentence when the token is refused. */
  onReplace: (workspace: ChatWorkspace, token: string) => Promise<void>;
  /** Rejects with the service's sentence when the app-level token is refused. */
  onSetAppToken: (workspace: ChatWorkspace, token: string) => Promise<void>;
  onRemoveAppToken: (workspace: ChatWorkspace) => void;
  onRemove: (workspace: ChatWorkspace) => void;
}) {
  return (
    <Table scroll className="hatch-chat-table">
      <thead>
        <tr>
          <th>Workspace</th>
          <th>Token</th>
          <th>App token</th>
          <th>Projects</th>
          <th>Sending</th>
          <th>Connected</th>
          <th />
        </tr>
      </thead>
      <tbody>
        {workspaces.map((workspace) => (
          <tr key={workspace.id}>
            <td className="hatch-chat-name">
              <strong>{workspace.name}</strong>
            </td>
            <td>{workspace.hasToken ? 'Token set' : <Text tone="muted">No token</Text>}</td>
            <td className="hatch-chat-apptoken-cell">
              <AppTokenCell
                workspace={workspace}
                busy={busy}
                onSet={onSetAppToken}
                onRemove={onRemoveAppToken}
              />
              <ListenState workspace={workspace} state={delivery.find((d) => d.workspaceId === workspace.id)} now={now} />
            </td>
            <td className="hatch-chat-projects-cell">
              <ChatBindings workspace={workspace} projects={projects} busy={busy} onChange={onBound} />
            </td>
            <td className="hatch-chat-delivery-cell">
              <DeliveryState state={delivery.find((d) => d.workspaceId === workspace.id)} now={now} />
            </td>
            <td>{agoPhrase(workspace.createdAt, now)}</td>
            <td className="hatch-chat-actions-cell">
              <div className="hatch-chat-actions">
                <ReplaceTokenForm workspace={workspace} busy={busy} onReplace={onReplace} />
                <Button variant="danger" disabled={busy} onClick={() => onRemove(workspace)}>
                  Remove
                </Button>
              </div>
            </td>
          </tr>
        ))}
      </tbody>
    </Table>
  );
}

/** What waits to be sent to a workspace, how many messages were dropped, and
    the last sentence Slack refused one with and when. A workspace with none of
    the three draws nothing. */
export function DeliveryState({ state, now }: { state: ChatWorkspaceDelivery | undefined; now: Date }) {
  if (!state) return null;
  const said = state.lastSaid && state.lastSaid.trim() !== '' ? state.lastSaid : null;
  if (state.waiting === 0 && state.dropped === 0 && !said) return null;

  return (
    <div className="hatch-chat-delivery">
      {state.waiting > 0 && <span>{state.waiting} waiting</span>}
      {state.dropped > 0 && <span className="text-danger">{state.dropped} dropped</span>}
      {said && (
        <span>
          Slack said: {said}
          {state.lastSaidAt && ` (${agoPhrase(state.lastSaidAt, now)})`}
        </span>
      )}
    </div>
  );
}

/** Whether Hatch is listening to a workspace. Three states, the first drawn by
    AppTokenCell: no app token draws nothing here. A readable token with an open
    connection and no run of failures reads Listening, with when it opened. Any
    other held token reads Not listening, red once it has failed, with when the
    failures began and the sentence Slack gave. An unreadable token's stored
    sentence is Hatch's own, which AppTokenCell already says, so it is not
    drawn as Slack's; and a stale open time on one never reads as listening. */
export function ListenState({
  workspace,
  state,
  now,
}: {
  workspace: ChatWorkspace;
  state: ChatWorkspaceDelivery | undefined;
  now: Date;
}) {
  const unreadable = workspace.appTokenUnreadable;
  if (!workspace.hasAppToken && !unreadable) return null;

  const since = state?.listeningSince ?? null;
  const failing = state?.listenFailingSince ?? null;
  if (!unreadable && since && !failing) {
    return (
      <div className="hatch-chat-listen">
        <span>Listening</span>
        <span>opened {agoPhrase(since, now)}</span>
      </div>
    );
  }

  const said = !unreadable && state?.listenLastSaid && state.listenLastSaid.trim() !== '' ? state.listenLastSaid : null;
  const tried = unreadable || failing || said;

  return (
    <div className="hatch-chat-listen">
      <span className={tried ? 'text-danger' : undefined}>{tried ? 'Not listening' : 'Not listening yet'}</span>
      {failing && <span>first failed {agoPhrase(failing, now)}</span>}
      {said && <span>Slack said: {said}</span>}
    </div>
  );
}

/** Said when no public address is set: a message still names its ticket, with no link. */
export function LinksOffNote() {
  return (
    <p className="hatch-chat-links-note">
      Messages name the ticket with no link. <code>HATCH_PUBLIC_URL</code> sets the address, and with it set they link.
    </p>
  );
}

/** A row's own inline input for a new token. It keeps its typing and its
    refusal to itself, so a refusal appears under the row it belongs to. */
export function ReplaceTokenForm({
  workspace,
  busy,
  onReplace,
}: {
  workspace: ChatWorkspace;
  busy: boolean;
  onReplace: (workspace: ChatWorkspace, token: string) => Promise<void>;
}) {
  const [token, setToken] = useState('');
  const [failure, setFailure] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (token.trim() === '') return;
    const typed = token;
    setToken('');
    try {
      await onReplace(workspace, typed);
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    }
  }

  return (
    <form className="hatch-chat-replace" onSubmit={(e) => void submit(e)}>
      <input
        type="password"
        autoComplete="off"
        spellCheck={false}
        placeholder="New token"
        aria-label={`New token for ${workspace.name}`}
        value={token}
        onChange={(e) => setToken(e.target.value)}
      />
      <Button type="submit" disabled={busy || token.trim() === ''}>
        Replace token
      </Button>
      {failure && <p className="text-danger hatch-chat-replace__failure">{failure}</p>}
    </form>
  );
}

/** A workspace's app-level token: which of three states it is in, a box to
    paste or replace it, and Remove while one is held. Like the bot token it is
    typed into a password input, emptied before the answer, and never drawn
    back: no placeholder or hint here looks like a token. Removing asks nothing,
    since the token can be pasted again. */
export function AppTokenCell({
  workspace,
  busy,
  onSet,
  onRemove,
}: {
  workspace: ChatWorkspace;
  busy: boolean;
  /** Rejects with the service's sentence when the token is refused. */
  onSet: (workspace: ChatWorkspace, token: string) => Promise<void>;
  onRemove: (workspace: ChatWorkspace) => void;
}) {
  const [token, setToken] = useState('');
  const [failure, setFailure] = useState<string | null>(null);
  const held = workspace.hasAppToken || workspace.appTokenUnreadable;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (token.trim() === '') return;
    const typed = token;
    setToken('');
    try {
      await onSet(workspace, typed);
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    }
  }

  return (
    <form className="hatch-chat-replace" onSubmit={(e) => void submit(e)}>
      {workspace.appTokenUnreadable ? (
        <p className="hatch-chat-apptoken__state text-danger">
          The app token cannot be read. Paste it again.
        </p>
      ) : workspace.hasAppToken ? (
        <p className="hatch-chat-apptoken__state">App token set</p>
      ) : (
        <Text tone="muted" className="hatch-chat-apptoken__state">
          No app token
        </Text>
      )}
      <input
        type="password"
        autoComplete="off"
        spellCheck={false}
        placeholder="App-level token"
        aria-label={`App-level token for ${workspace.name}`}
        value={token}
        onChange={(e) => setToken(e.target.value)}
      />
      <Button type="submit" disabled={busy || token.trim() === ''}>
        {workspace.hasAppToken ? 'Replace app token' : 'Set app token'}
      </Button>
      {held && (
        <Button type="button" disabled={busy} onClick={() => onRemove(workspace)}>
          Remove app token
        </Button>
      )}
      {failure && <p className="text-danger hatch-chat-replace__failure">{failure}</p>}
    </form>
  );
}

/** The question Remove waits on. Only its confirm button deletes. */
export function RemoveDialog({
  workspace,
  busy,
  onClose,
  onConfirm,
}: {
  workspace: ChatWorkspace | null;
  busy: boolean;
  onClose: () => void;
  onConfirm: (workspace: ChatWorkspace) => void;
}) {
  return (
    <Modal open={workspace !== null} onClose={onClose} title={`Remove ${workspace?.name ?? ''}?`}>
      {workspace && (
        <div className="hatch-claim-clear">
          <p>
            This disconnects {workspace.name} and removes its token and everything Hatch holds for it. Nothing in the
            workspace itself is touched, and it can be connected again with a token.
          </p>
          <div className="hatch-form-actions">
            <Button onClick={onClose}>Leave it</Button>
            <Button variant="danger" loading={busy} onClick={() => onConfirm(workspace)}>
              Remove
            </Button>
          </div>
        </div>
      )}
    </Modal>
  );
}
