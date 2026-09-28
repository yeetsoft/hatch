import { Badge } from '@hatch/ui';
import type { BadgeTone } from '@hatch/ui';
import type { MessageStatus } from '../lib/messages';

/* Read is the good news, and waiting is the state somebody is watching for, so
   it is the one with an accent. Held is a message waiting for a session that
   does not exist yet - a fact, not a fault - and stays at rest. */
const TONES: Record<MessageStatus['state'], BadgeTone> = {
  read: 'success',
  waiting: 'primary',
  held: 'muted',
};

/**
 * What became of a message to the agent, said in words: the same badge on the
 * Claim panel and in the thread, so the two cannot come to say different things.
 */
export function MessageState({ status }: { status: MessageStatus | null }) {
  // Null is anything that is not a message: a note shows no read state.
  if (!status) return null;

  return <Badge tone={TONES[status.state]}>{status.words}</Badge>;
}
