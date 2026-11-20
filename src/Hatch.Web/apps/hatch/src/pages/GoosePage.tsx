import { useCallback } from 'react';
import { Card, EmptyState, PageHeader } from '@hatch/ui';
import { getGooseFindings, getGooseRuns } from '../api/client';
import { GooseFinding } from '../components/GooseFinding';
import { GooseRun } from '../components/GooseRun';
import {
  closedFindings,
  findingsOpenAt,
  gooseStateWords,
  noClosedFindingsWords,
  noOpenFindingsWords,
  noRunsWords,
  openFindings,
  runsCoverWholeBoard,
  runsCoverWholeBoardWords,
  runsNewestFirst,
} from '../lib/goose';
import { useAttentionContext } from '../lib/useAttentionContext';
import { useAttentionNow } from '../lib/useAttentionNow';
import { POLL_MS } from '../lib/useAttention';
import { useLoaded } from '../lib/useLoaded';
import { useMe } from '../lib/useMe';
import type { GooseFinding as Finding, GooseRun as Run } from '../types';

interface GooseRecord {
  runs: Run[];
  findings: Finding[];
}

/**
 * What the goose has been doing: whether it is running, what is open, its runs
 * newest first, and what has closed.
 *
 * Refreshed on the attention control's own interval. A caller whose scope is
 * narrower reads the findings of their projects and no runs - a run holds what
 * was seen across the whole board - so the runs are not asked for, and one line
 * says why they are missing.
 */
export function GoosePage() {
  const { attention, reload: reloadAttention } = useAttentionContext();
  const { me } = useMe();
  const now = useAttentionNow(true);

  // A caller not yet answered, or the nobody a key is, asks for the runs: the
  // server answers a narrowed caller an empty list, so nothing leaks either way.
  const coversWholeBoard = runsCoverWholeBoard(me);

  const load = useCallback(async (): Promise<GooseRecord> => {
    const [findings, runs] = await Promise.all([getGooseFindings(), coversWholeBoard ? getGooseRuns() : Promise.resolve([])]);
    return { findings, runs };
  }, [coversWholeBoard]);

  const { data, error, reload } = useLoaded<GooseRecord>(load, { everyMs: POLL_MS });

  const state = gooseStateWords(attention?.goose, now);
  const open = data ? openFindings(data.findings) : [];
  const closed = data ? closedFindings(data.findings) : [];
  const runs = data ? runsNewestFirst(data.runs) : [];

  const filed = () => {
    void reload();
    void reloadAttention();
  };

  return (
    <div className="hatch-page">
      <PageHeader title="Goose" description="What the goose has been doing: what is open, its runs, and what has closed." />

      {error && <p className="text-danger">{error}</p>}

      {state && (
        <p className={attention?.goose?.stopped ? 'hatch-goose-state text-danger' : 'hatch-goose-state'}>{state}</p>
      )}

      {data && (
        <>
          <section className="hatch-goose-section" aria-labelledby="goose-open">
            <h2 id="goose-open" className="hatch-goose-heading">
              Open findings
            </h2>
            {open.length === 0 ? (
              <EmptyState message={noOpenFindingsWords()} />
            ) : (
              <Card flush>
                <ul className="hatch-goose-rows">
                  {open.map((f) => (
                    <li key={f.id}>
                      <GooseFinding finding={f} now={now} refreshed={data} onFiled={filed} />
                    </li>
                  ))}
                </ul>
              </Card>
            )}
          </section>

          <section className="hatch-goose-section" aria-labelledby="goose-runs">
            <h2 id="goose-runs" className="hatch-goose-heading">
              Runs
            </h2>
            {!coversWholeBoard ? (
              <p className="hatch-goose-note">{runsCoverWholeBoardWords()}</p>
            ) : runs.length === 0 ? (
              <EmptyState message={noRunsWords()} />
            ) : (
              <Card flush>
                <ul className="hatch-goose-rows">
                  {runs.map((r) => (
                    <li key={r.id}>
                      <GooseRun run={r} open={findingsOpenAt(data.findings, r)} now={now} />
                    </li>
                  ))}
                </ul>
              </Card>
            )}
          </section>

          <section className="hatch-goose-section" aria-labelledby="goose-closed">
            <h2 id="goose-closed" className="hatch-goose-heading">
              Closed findings
            </h2>
            {closed.length === 0 ? (
              <EmptyState message={noClosedFindingsWords()} />
            ) : (
              <Card flush>
                <ul className="hatch-goose-rows">
                  {closed.map((f) => (
                    <li key={f.id}>
                      <GooseFinding finding={f} now={now} refreshed={data} onFiled={filed} />
                    </li>
                  ))}
                </ul>
              </Card>
            )}
          </section>
        </>
      )}
    </div>
  );
}
