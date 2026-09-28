import { useState } from 'react';
import { Badge, Button, Card } from '@hatch/ui';
import { agoPhrase, claimHealth } from '../lib/claim';
import { renderMarkdown } from '../lib/markdown';
import { messageState } from '../lib/messages';
import { useAutoGrow } from '../lib/useAutoGrow';
import type { Comment, IssueClaim } from '../types';
import { MessageState } from './MessageState';

/**
 * Who is holding this ticket right now, from where, since when, and the last
 * thing they said.
 *
 * Above everything the page lets you change, for the reason `Waiting` sits
 * there: it is a fact about the ticket that something else is acting on it at
 * this moment, and knowing that before pressing anything is the point of
 * drawing it at all.
 *
 * Presentational - the busy flag, the error line and the re-read are the page's,
 * because a claim is one fact on a page made of them and its failures belong in
 * the one error line the page already has.
 *
 * Nothing at all where there is no claim, not an empty section. "A heading over
 * a blank space is a page saying 'this feature exists and has failed you'" is
 * WorkLog's comment and it applies unchanged, more so here: the overwhelming
 * majority of issues are held by nobody, so this section costs them nothing.
 *
 * The box under the facts talks to the session holding the claim, and each
 * message below it says whether that session has read it - "read" meaning put
 * into its context, which is all that can be known. The panel does not poll
 * itself: the page re-reads every five seconds while a message is waiting and
 * for no other reason, so the state changes here without a reload and stops
 * changing the moment there is nothing left to wait for.
 */
export function ClaimPanel({
  issueKey,
  claim,
  messages,
  sending,
  onSend,
  onClear,
}: {
  issueKey: string;
  claim: IssueClaim | null;
  /** What `claimMessages` chose: every unread message, and every one read since this claim was taken. */
  messages: Comment[];
  /** A message is on its way to the server. */
  sending: boolean;
  /** Sends the message, and answers whether it went - the box is emptied only then, so a refusal keeps what was typed. */
  onSend: (body: string) => Promise<boolean>;
  /** Opens the speed bump. Not the clear itself - see ClearClaimDialog. */
  onClear: () => void;
}) {
  const [body, setBody] = useState('');
  const box = useAutoGrow(body);

  if (!claim) return null;

  async function send() {
    if (await onSend(body)) setBody('');
  }

  // Once per render, as ClaimBadge does, and never read again.
  const now = new Date();
  const health = claimHealth(claim, now);

  return (
    <Card className={health === 'quiet' ? 'hatch-claim hatch-claim-quiet' : 'hatch-claim'}>
      <div className="hatch-claim-head">
        <h2 className="hatch-section-title">Claim</h2>
        {/* The whole block carries the state, so it reads as wanting attention
            rather than one word inside it doing. Said out loud as well, because
            an amber edge is not a sentence. */}
        {health === 'quiet' && <Badge tone="danger">not heard from lately</Badge>}
      </div>

      <p className="hatch-claim-holder">
        <strong>{claim.claimedBy}</strong> is working {issueKey} from <code>{claim.runner}</code>
      </p>

      {/* Elapsed words rather than timestamps: the question a claim raises is
          "is this still going", and an instant is an arithmetic problem
          somebody has to do before they can answer it. */}
      <div className="hatch-claim-facts">
        <span className="text-muted">taken {agoPhrase(claim.claimedAt, now)}</span>
        <span className={health === 'quiet' ? undefined : 'text-muted'}>
          last heard from {agoPhrase(claim.heartbeatAt, now)}
        </span>
      </div>

      {/* Absent entirely where the runner has not said anything - the same
          register as the section itself. A quoted blank is worse than silence. */}
      {claim.chatter && (
        <p className="hatch-claim-chatter">
          &ldquo;{claim.chatter}&rdquo; <span className="text-muted">{agoPhrase(claim.chatterAt, now)}</span>
        </p>
      )}

      {/* Talking to the agent is the point of the panel, so it sits above the
          button that takes the ticket away from it. */}
      <div className="hatch-claim-tell">
        <label className="hatch-claim-tell-label" htmlFor="hatch-claim-tell">
          Tell the agent
        </label>
        <div className="hatch-comment-box">
          <textarea
            id="hatch-claim-tell"
            ref={box}
            className="hatch-grows"
            rows={2}
            value={body}
            placeholder="Read at its next step, and again before it stops."
            onChange={(e) => setBody(e.target.value)}
            // Meta/Ctrl+Enter sends, as the answer box does. A bare Enter stays
            // a newline: an instruction with a caveat under it is a better one.
            onKeyDown={(e) => {
              if (e.key === 'Enter' && (e.metaKey || e.ctrlKey) && body.trim() && !sending) void send();
            }}
          />
          <Button variant="primary" loading={sending} disabled={!body.trim()} onClick={() => void send()}>
            Send
          </Button>
        </div>

        {messages.length > 0 && (
          <ul className="hatch-claim-messages">
            {messages.map((message) => (
              <li key={message.id} className="hatch-claim-message">
                <div className="hatch-comment-head">
                  <strong>{message.author}</strong>
                  <span className="text-muted">{new Date(message.createdAt).toLocaleString()}</span>
                  <MessageState status={messageState(message, claim, issueKey, now)} />
                </div>
                <div
                  className="hatch-markdown"
                  dangerouslySetInnerHTML={{ __html: renderMarkdown(message.body) }}
                />
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="hatch-form-actions">
        <Button onClick={onClear}>Clear the claim</Button>
      </div>
    </Card>
  );
}
