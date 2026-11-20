import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getEvents, getIssue, getStatuses, getWorkLog } from '../api/client';
import { message } from '../lib/errors';
import { agoWords, bugColumnName, sessionReports } from '../lib/goose';
import { ProseClamp } from './ProseClamp';
import type { Issue, IssueEvent, Status, WorkLogEntry } from '../types';

interface Loaded {
  issue: Issue;
  statuses: Status[];
  entries: WorkLogEntry[];
  events: IssueEvent[];
}

/**
 * What came of a finding handed to an agent: the bug's key and column, the
 * brief it was filed with, and what each session on it reported.
 *
 * Nothing here is stored for the finding. The brief is the bug's description,
 * the reports are its work log, and the runner is read off the event that
 * released each session - see `sessionReports`.
 *
 * Reads again whenever `refreshed` changes, which is the page's own poll, and
 * keeps what it drew meanwhile so the region does not blink.
 */
export function GooseFindingBug({ bugKey, refreshed, now }: { bugKey: string; refreshed: unknown; now: Date }) {
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    Promise.all([getIssue(bugKey), getStatuses(), getWorkLog(bugKey), getEvents(bugKey)]).then(
      ([issue, statuses, log, events]) => {
        if (!alive) return;
        setLoaded({ issue, statuses, entries: log.entries, events });
        setError(null);
      },
      (err) => {
        if (alive) setError(message(err));
      },
    );
    return () => {
      alive = false;
    };
  }, [bugKey, refreshed]);

  if (error && !loaded) return <p className="text-danger">{error}</p>;
  if (!loaded) return <p className="hatch-goose-note">Reading the bug.</p>;

  const column = bugColumnName(loaded.statuses, loaded.issue);
  const reports = sessionReports(loaded.entries, loaded.events);

  return (
    <section className="hatch-goose-bug" aria-label={`Bug ${bugKey}`}>
      <h4 className="hatch-goose-subheading">
        <Link to={`/issues/${bugKey}`}>{bugKey}</Link>
        {column && <span className="hatch-goose-column">{column}</span>}
      </h4>

      <h5 className="hatch-goose-label">The brief</h5>
      {loaded.issue.description.trim() === '' ? (
        <p className="hatch-goose-note">The bug has no description.</p>
      ) : (
        <ProseClamp source={loaded.issue.description} limit={4} />
      )}

      <h5 className="hatch-goose-label">What each session reported</h5>
      {reports.length === 0 ? (
        <p className="hatch-goose-note">No session has worked on it yet.</p>
      ) : (
        <ul className="hatch-goose-reports">
          {reports.map((r) => (
            <li key={r.id}>
              <span className="hatch-goose-report-title">{r.title}</span>
              {r.summary && <span className="hatch-goose-report-summary">{r.summary}</span>}
              <span className="hatch-goose-meta" title={new Date(r.at).toLocaleString()}>
                {agoWords(r.at, now)}
                {r.runner && ` · ${r.runner}`}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
