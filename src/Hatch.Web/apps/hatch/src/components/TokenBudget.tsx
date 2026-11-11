import type { CSSProperties } from 'react';
import { Text } from '@hatch/ui';
import { sessionTokenBudget } from '../lib/runners';
import type { RunnerSession } from '../types';

/** A session's token budget: the night's line, then the hour's, each with a
    bar where it has a cap. Copied from the old runners page's own
    `TokenBudget`, widened from a runner to a session - see `sessionTokenBudget`. */
export function TokenBudget({ session }: { session: RunnerSession }) {
  const lines = sessionTokenBudget(session);
  if (lines.length === 0) return <Text tone="muted">No token budget</Text>;

  return (
    <>
      {lines.map((line) => (
        <div key={line.text} className="hatch-token-budget-line">
          <span>{line.text}</span>
          {line.percent !== null && (
            <div className="hatch-usage-bar-track" role="img" aria-label={line.text}>
              <div
                className="hatch-usage-bar-fill"
                style={{ '--usage-color': `var(--${line.tone})`, width: `${line.percent}%` } as CSSProperties}
              />
            </div>
          )}
        </div>
      ))}
    </>
  );
}
