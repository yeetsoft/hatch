import { Link } from 'react-router-dom';
import { Text } from '@hatch/ui';
import { agoPhrase } from '../lib/claim';
import { sessionActivity, sessionParts } from '../lib/runners';
import { moneyPhrase } from '../lib/workLog';
import { EffectiveBounds } from './EffectiveBounds';
import { MachineDetails } from './MachineDetails';
import { RunnerGlyph } from './RunnerGlyph';
import { TokenBudget } from './TokenBudget';
import type { Runner, RunnerSession } from '../types';

/**
 * One live session, as a `<li>` under its runner - the caller owns the
 * `<ul>` so a multi-host note can sit between it and the list.
 *
 * No controls here (HA-2149's own scope): that is a later task's slot,
 * marked below, and pausing or stopping is per session rather than per
 * runner.
 */
export function SessionBlock({ session, runner, now }: { session: RunnerSession; runner: Runner; now: Date }) {
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

      {/* Controls - Pause/Resume, Stop after this one - are a later task. */}
    </li>
  );
}
