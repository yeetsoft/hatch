import { Link } from 'react-router-dom';
import { Text } from '@hatch/ui';
import { agoPhrase } from '../lib/claim';
import { sessionActivity, sessionParts } from '../lib/runners';
import type { SessionActions } from '../lib/sessionActions';
import { moneyPhrase } from '../lib/workLog';
import { EffectiveBounds } from './EffectiveBounds';
import { MachineDetails } from './MachineDetails';
import { RunnerGlyph } from './RunnerGlyph';
import { SessionControls } from './SessionControls';
import { TokenBudget } from './TokenBudget';
import type { Runner, RunnerSession } from '../types';

/**
 * One live session, as a `<li>` under its runner - the caller owns the
 * `<ul>` so a multi-host note can sit between it and the list.
 *
 * Its controls are per session rather than per runner: `SessionControls`
 * acts on this one session alone, and hands a refusal up to the card.
 */
export function SessionBlock({
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
  const activity = sessionActivity(session, runner, now);
  const { words, claimKey } = sessionParts(session, runner, now);

  return (
    <li className="hatch-runner-session">
      <RunnerGlyph state={activity} label={words} />
      {session.where != null && <span className="hatch-runner-name">{session.where}</span>}
      <Text tone="muted">{words}</Text>
      <Text tone="muted">{agoPhrase(session.lastSeenAt, now)}</Text>

      {claimKey ? (
        <>
          <Link className="hatch-card-key" to={`/issues/${claimKey}`}>
            {claimKey}
          </Link>
          {session.line && <Text tone="muted">{session.line}</Text>}
        </>
      ) : (
        <Text tone="muted">nothing yet</Text>
      )}

      <Text tone="muted">{session.runs ?? 0} run(s)</Text>
      <Text tone="muted">{moneyPhrase(session.spent ?? 0)}</Text>
      <TokenBudget session={session} />
      <EffectiveBounds session={session} />

      <MachineDetails session={session} runner={runner} now={now} />

      <SessionControls session={session} runner={runner} now={now} actions={actions} onRefusal={onRefusal} />
    </li>
  );
}
