import { useState } from 'react';
import { Button, Text } from '@hatch/ui';
import { isIssueKeyShape, orderSentence, sessionControllable, sessionOrder } from '../lib/runners';
import type { SessionActions } from '../lib/sessionActions';
import type { Runner, RunnerSession } from '../types';

/**
 * One live session's controls - Pause or Resume, Stop after this one, and the
 * order control - for the one session it is given, never its siblings.
 *
 * A `once` session says "one increment" in place of buttons, as a runner's row
 * does: a row with no buttons reads as a bug otherwise. A caller who may not
 * control it gets nothing here - the card says once, above the list, whose it
 * is to control. A press hands its refusal, or null, to `onRefusal`: the card
 * draws it, not this.
 */
export function SessionControls({
  session,
  runner,
  now,
  actions,
  onRefusal,
}: {
  session: RunnerSession;
  runner: Runner;
  now: Date;
  actions: SessionActions;
  onRefusal: (refusal: string | null) => void;
}) {
  const [key, setKey] = useState('');

  if (session.kind !== 'loop') return <Text tone="muted">one increment</Text>;
  if (!sessionControllable(runner, session)) return null;

  const stopping = session.state === 'stopping';
  const paused = session.state === 'paused';
  const order = sessionOrder(session);
  const press = async (action: Promise<string | null>) => onRefusal(await action);

  return (
    <div className="hatch-reorder hatch-reorder--wrap">
      <Button onClick={() => press(paused ? actions.resume(session) : actions.pause(session))} disabled={stopping}>
        {paused ? 'Resume' : 'Pause'}
      </Button>
      <Button variant="danger" onClick={() => press(actions.stop(session))} disabled={stopping}>
        Stop after this one
      </Button>
      {order ? (
        <>
          <Text tone="muted">{orderSentence(order, now)}</Text>
          <Button onClick={() => press(actions.withdraw(session))}>Withdraw</Button>
        </>
      ) : (
        <>
          <input
            aria-label="Ticket to work"
            placeholder="HA-12"
            value={key}
            disabled={stopping}
            onChange={(e) => setKey(e.target.value)}
          />
          <Button
            disabled={stopping || !isIssueKeyShape(key)}
            onClick={() => {
              const ticket = key.trim();
              setKey('');
              return press(actions.order(session, { kind: 'work', key: ticket }));
            }}
          >
            Work this ticket
          </Button>
          <Button disabled={stopping} onClick={() => press(actions.order(session, { kind: 'pass' }))}>
            Run one pass
          </Button>
        </>
      )}
    </div>
  );
}
