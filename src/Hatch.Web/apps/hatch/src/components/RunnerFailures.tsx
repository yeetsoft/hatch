import { useEffect, useState } from 'react';
import { Card } from '@hatch/ui';
import { getRunnerFailures } from '../api/client';
import { earlierWords, failuresNeedWhere, failureWords } from '../lib/runners';
import type { Runner, RunnerFailure } from '../types';

/**
 * The two lists on a runner's page: what it cannot do now and since when, and
 * what it could not do before. A list with no rows draws no heading, and a
 * runner with neither draws nothing at all.
 *
 * The view is pure so it can be drawn without a fetch; `RunnerFailures` below
 * holds the read.
 */
export function RunnerFailuresView({
  current,
  earlier,
  now,
}: {
  current: RunnerFailure[];
  earlier: RunnerFailure[];
  now: Date;
}) {
  if (current.length === 0 && earlier.length === 0) return null;

  const currentWhere = failuresNeedWhere(current);
  const earlierWhere = failuresNeedWhere(earlier);

  return (
    <Card>
      {current.length > 0 && (
        <>
          <h2 className="hatch-section-title">Failing</h2>
          <ul className="hatch-runner-failing">
            {current.map((f) => (
              <li key={`${f.kind}|${f.where ?? ''}`} className="text-danger">
                {failureWords(f, now, currentWhere)}
              </li>
            ))}
          </ul>
        </>
      )}
      {earlier.length > 0 && (
        <>
          <h2 className="hatch-section-title">Earlier</h2>
          <ul className="hatch-runner-earlier">
            {earlier.map((f) => (
              <li key={`${f.kind}|${f.where ?? ''}|${f.beganAt}`}>{earlierWords(f, now, earlierWhere)}</li>
            ))}
          </ul>
        </>
      )}
    </Card>
  );
}

/**
 * Failing comes from the runner the layout already polls, so it never lags the
 * row's badge. Earlier is read on mount and again whenever the number of
 * current failures changes - a failure that ends moves from one list to the
 * other. A failed read keeps the last answer.
 */
export function RunnerFailures({ runner }: { runner: Runner }) {
  const [earlier, setEarlier] = useState<RunnerFailure[]>([]);
  const count = runner.failures?.length;

  useEffect(() => {
    let ignore = false;
    getRunnerFailures(runner.name)
      .then((answer) => {
        if (!ignore) setEarlier(answer.earlier);
      })
      .catch(() => {});
    return () => {
      ignore = true;
    };
  }, [runner.name, count]);

  return <RunnerFailuresView current={runner.failures ?? []} earlier={earlier} now={new Date()} />;
}
