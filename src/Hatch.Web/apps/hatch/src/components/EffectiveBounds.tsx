import type { ReactNode } from 'react';
import { Fragment } from 'react';
import { Link } from 'react-router-dom';
import { Text } from '@hatch/ui';
import { compactTokens } from '../lib/workLog';
import type { RunnerSession } from '../types';

/** A session's own narrowed bounds, as `BoundsSummary`'s mirror minus
    tags/projects/mine/dangerous/"(default)" - those are a runner's own
    facts, not a session's narrowing of them. Undefined counts as unset, the
    same as null. */
export function EffectiveBounds({ session }: { session: RunnerSession }) {
  const said: ReactNode[] = [
    session.under != null && (
      <>
        under{' '}
        <Link className="hatch-card-key" to={`/issues/${session.under}`}>
          {session.under}
        </Link>
      </>
    ),
    session.maxRuns != null && `${session.maxRuns} run(s)`,
    session.maxSpend != null && `$${session.maxSpend}`,
    session.maxTokens != null && `${compactTokens(session.maxTokens)} tokens`,
    session.maxTokensPerHour != null && `${compactTokens(session.maxTokensPerHour)} tokens/hour`,
    session.untilAt != null &&
      `until ${new Date(session.untilAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`,
  ].filter(Boolean);

  return said.length === 0 ? (
    <Text tone="muted">no bounds</Text>
  ) : (
    <>
      {said.map((part, i) => (
        <Fragment key={i}>
          {i > 0 && ', '}
          {part}
        </Fragment>
      ))}
    </>
  );
}
