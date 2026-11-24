import { useEffect, useState } from 'react';
import { Button, Card, Modal } from '@hatch/ui';
import { getChatMembers, getChatSuggestion, linkChatMember, unlinkChat } from '../api/client';
import { chatSectionApplies, filterMembers, LINKS_OFF_WORDS, memberWords, refusalWords, standing } from '../lib/chatLinks';
import type { ConfirmOutcome } from '../lib/chatLinks';
import type { MeChat, MeChatLink, MeChatMember, MeChatWorkspace } from '../types';

/**
 * A person's Slack links, on the Settings page: one row per workspace with where
 * the link stands, and *Link* or *Unlink*. Drawn only where there is a workspace
 * to link in.
 *
 * *Link* asks the server for the member whose email the person signed in with.
 * Offered one, they link as that member or pick someone else; offered none, they
 * go straight to the picker. The server then sends that member a message with a
 * link in it, and the row reads *waiting* until it is opened.
 *
 * `notice` is what the confirm page says happened, drawn above the rows.
 * `onChanged` reloads the list after every write.
 */
export function ChatAccountsCard({
  chat,
  notice = null,
  now,
  onChanged,
}: {
  chat: MeChat | null;
  notice?: ConfirmOutcome | null;
  now: number;
  onChanged: () => void;
}) {
  const [linking, setLinking] = useState<number | null>(null);
  const [unlinkOf, setUnlinkOf] = useState<{ workspace: MeChatWorkspace; link: MeChatLink } | null>(null);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  if (!chatSectionApplies(chat)) {
    return notice ? <ChatLinkNotice notice={notice} /> : null;
  }

  async function unlink(link: MeChatLink) {
    setBusy(true);
    try {
      await unlinkChat(link.id);
      setFailure(null);
    } catch (err) {
      setFailure(refusalWords(err));
    } finally {
      setBusy(false);
      setUnlinkOf(null);
      onChanged();
    }
  }

  return (
    <>
      {notice && <ChatLinkNotice notice={notice} />}
      <Card>
        <h2 className="hatch-section-title">Slack accounts</h2>
        <p className="text-muted">
          Link your Slack account so Hatch knows which Slack member is you. Hatch sends that member a message with a
          link in it, and the account is linked once you open it.
        </p>
        {!chat.linksOn && <p>{LINKS_OFF_WORDS}</p>}
        <ul className="hatch-slack-accounts">
          {chat.workspaces.map((workspace) => (
            <AccountRow
              key={workspace.id}
              workspace={workspace}
              linksOn={chat.linksOn}
              now={now}
              open={linking === workspace.id}
              onLink={() => {
                setFailure(null);
                setLinking(workspace.id);
              }}
              onClose={() => setLinking(null)}
              onUnlink={(link) => setUnlinkOf({ workspace, link })}
              onLinked={() => {
                setLinking(null);
                onChanged();
              }}
            />
          ))}
        </ul>
        {failure && <p className="text-danger">{failure}</p>}
      </Card>
      <UnlinkDialog
        target={unlinkOf}
        busy={busy}
        onClose={() => setUnlinkOf(null)}
        onConfirm={(link) => void unlink(link)}
      />
    </>
  );
}

/** What the confirm page says: the member linked, or why it did not work. */
export function ChatLinkNotice({ notice }: { notice: ConfirmOutcome }) {
  return (
    <p role="status" className={notice.failed ? 'text-danger' : undefined}>
      {notice.words}
    </p>
  );
}

/** One workspace: its name, where the link stands, and the buttons that fit. */
export function AccountRow({
  workspace,
  linksOn,
  now,
  open,
  onLink,
  onClose,
  onUnlink,
  onLinked,
}: {
  workspace: MeChatWorkspace;
  linksOn: boolean;
  now: number;
  open: boolean;
  onLink: () => void;
  onClose: () => void;
  onUnlink: (link: MeChatLink) => void;
  onLinked: () => void;
}) {
  const state = standing(workspace, now);
  const link = workspace.link;
  return (
    <li className="hatch-slack-account">
      <div className="hatch-slack-account__head">
        <div className="hatch-slack-account__who">
          <strong>{workspace.name}</strong>
          <span className="text-muted">{state.words}</span>
        </div>
        <div className="hatch-slack-account__actions">
          {linksOn && state.canLink && (
            <Button onClick={onLink} disabled={open}>
              Link
            </Button>
          )}
          {link && state.canUnlink && <Button onClick={() => onUnlink(link)}>Unlink</Button>}
        </div>
      </div>
      {open && <LinkFlow workspace={workspace} onClose={onClose} onLinked={onLinked} />}
    </li>
  );
}

type Phase = 'asking' | 'suggested' | 'picking';

/** Asking who the person is, then offering a member or the picker. */
function LinkFlow({
  workspace,
  onClose,
  onLinked,
}: {
  workspace: MeChatWorkspace;
  onClose: () => void;
  onLinked: () => void;
}) {
  const [phase, setPhase] = useState<Phase>('asking');
  const [suggested, setSuggested] = useState<MeChatMember | null>(null);
  const [members, setMembers] = useState<MeChatMember[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  function pick() {
    setPhase('picking');
    setFailure(null);
    // Read once, when the picker opens; typing filters what is already here.
    getChatMembers(workspace.id)
      .then(setMembers)
      .catch((err) => setFailure(refusalWords(err)));
  }

  useEffect(() => {
    let live = true;
    getChatSuggestion(workspace.id)
      .then((s) => {
        if (!live) return;
        if (s.member) {
          setSuggested(s.member);
          setPhase('suggested');
        } else {
          pick();
        }
      })
      .catch((err) => live && setFailure(refusalWords(err)));
    return () => {
      live = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once per opening
  }, [workspace.id]);

  async function choose(member: MeChatMember) {
    setBusy(true);
    try {
      await linkChatMember(workspace.id, member.id);
      onLinked();
    } catch (err) {
      setFailure(refusalWords(err));
      setBusy(false);
    }
  }

  return (
    <div className="hatch-slack-flow">
      {phase === 'asking' && !failure && <p className="text-muted">Looking for your Slack account…</p>}
      {phase === 'suggested' && suggested && (
        <SuggestionPrompt
          workspaceName={workspace.name}
          member={suggested}
          busy={busy}
          onAccept={() => void choose(suggested)}
          onOther={pick}
        />
      )}
      {phase === 'picking' && <MemberPicker members={members} busy={busy} onChoose={(m) => void choose(m)} />}
      {failure && <p className="text-danger">{failure}</p>}
      <div className="hatch-form-actions">
        <Button onClick={onClose}>Cancel</Button>
      </div>
    </div>
  );
}

/** The member Hatch found by the person's email: link as them, or pick another. */
export function SuggestionPrompt({
  workspaceName,
  member,
  busy,
  onAccept,
  onOther,
}: {
  workspaceName: string;
  member: MeChatMember;
  busy: boolean;
  onAccept: () => void;
  onOther: () => void;
}) {
  const words = memberWords(member);
  return (
    <div>
      <p>
        Is <strong>{words.name}</strong>
        {words.displayName && <span className="text-muted"> ({words.displayName})</span>} you on {workspaceName}?
      </p>
      <div className="hatch-form-actions">
        <Button variant="primary" loading={busy} onClick={onAccept}>
          Link as {words.name}
        </Button>
        <Button disabled={busy} onClick={onOther}>
          Pick someone else
        </Button>
      </div>
    </div>
  );
}

/** Every Slack member in the workspace, filtered in the browser as the person types. */
export function MemberPicker({
  members,
  busy,
  initialQuery = '',
  onChoose,
}: {
  members: MeChatMember[] | null;
  busy: boolean;
  initialQuery?: string;
  onChoose: (member: MeChatMember) => void;
}) {
  const [query, setQuery] = useState(initialQuery);
  if (members === null) return <p className="text-muted">Reading the members…</p>;
  const shown = filterMembers(members, query);
  return (
    <div className="hatch-slack-picker">
      <label>
        <span className="text-muted">Find yourself</span>
        <input
          type="search"
          value={query}
          placeholder="Type a name"
          autoComplete="off"
          spellCheck={false}
          onChange={(e) => setQuery(e.target.value)}
        />
      </label>
      {shown.length === 0 ? (
        <p className="text-muted">Nobody matches.</p>
      ) : (
        <ul className="hatch-slack-members">
          {shown.map((member) => {
            const words = memberWords(member);
            return (
              <li key={member.id}>
                <button type="button" disabled={busy} onClick={() => onChoose(member)}>
                  <span>{words.name}</span>
                  {words.displayName && <span className="text-muted">{words.displayName}</span>}
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

/** Asks before a link is removed. Shaped as the Chat page's remove dialog is, but
    for a link, not a workspace. */
export function UnlinkDialog({
  target,
  busy,
  onClose,
  onConfirm,
}: {
  target: { workspace: MeChatWorkspace; link: MeChatLink } | null;
  busy: boolean;
  onClose: () => void;
  onConfirm: (link: MeChatLink) => void;
}) {
  return (
    <Modal open={target !== null} onClose={onClose} title={`Unlink ${target?.workspace.name ?? ''}?`}>
      {target && (
        <div className="hatch-claim-clear">
          <p>
            Hatch will stop treating {target.link.memberName} as you on {target.workspace.name}. Nothing in Slack itself
            is touched, and you can link again whenever you like.
          </p>
          <div className="hatch-form-actions">
            <Button onClick={onClose}>Leave it</Button>
            <Button variant="danger" loading={busy} onClick={() => onConfirm(target.link)}>
              Unlink
            </Button>
          </div>
        </div>
      )}
    </Modal>
  );
}
