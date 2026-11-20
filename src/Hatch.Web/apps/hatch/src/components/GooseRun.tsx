import { useEffect, useState } from 'react';
import { getGooseRun } from '../api/client';
import { message } from '../lib/errors';
import { runRowWords } from '../lib/goose';
import { GooseRow } from './GooseRow';
import type { GooseRun as Run, GooseRunDetail } from '../types';

/** What each check of a run looked at and concluded, or the error it failed with. */
function RunChecks({ run }: { run: Run }) {
  const [detail, setDetail] = useState<GooseRunDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Again when the run takes another sweep: its entries are the latest one's.
  useEffect(() => {
    let alive = true;
    getGooseRun(run.id).then(
      (d) => {
        if (!alive) return;
        setDetail(d);
        setError(null);
      },
      (err) => {
        if (alive) setError(message(err));
      },
    );
    return () => {
      alive = false;
    };
  }, [run.id, run.lastRanAt, run.sweeps]);

  if (error && !detail) return <p className="text-danger">{error}</p>;
  if (!detail) return <p className="hatch-goose-note">Reading the run.</p>;
  if (detail.entries.length === 0) return <p className="hatch-goose-note">This run holds no checks.</p>;

  return (
    <ul className="hatch-goose-checks">
      {detail.entries.map((e) => (
        <li key={e.check} className={e.failed ? 'hatch-goose-check hatch-goose-check--failed' : 'hatch-goose-check'}>
          <span className="hatch-goose-check-name">{e.check}</span>
          {e.failed ? (
            <span className="text-danger">Failed: {e.error ?? 'no reason was given'}</span>
          ) : (
            <dl className="hatch-goose-pairs">
              <div className="hatch-goose-pair">
                <dt>Looked at</dt>
                <dd>{e.looked}</dd>
              </div>
              <div className="hatch-goose-pair">
                <dt>Concluded</dt>
                <dd>{e.concluded}</dd>
              </div>
            </dl>
          )}
        </li>
      ))}
    </ul>
  );
}

/** One run of sweeps: its row, and the checks it holds once opened. */
export function GooseRun({ run, open, now }: { run: Run; open: number; now: Date }) {
  const words = runRowWords(run, open, now);

  return (
    <GooseRow
      head={
        <>
          <span
            className="hatch-goose-title"
            title={`${new Date(run.firstRanAt).toLocaleString()} to ${new Date(run.lastRanAt).toLocaleString()}`}
          >
            {words.when.charAt(0).toUpperCase() + words.when.slice(1)}
          </span>
          <span className="hatch-goose-meta">
            {[words.sweeps, words.slowest, words.checks, words.open].filter((w) => w !== '').join(' · ')}
          </span>
        </>
      }
    >
      <RunChecks run={run} />
    </GooseRow>
  );
}
