import { Badge } from '@hatch/ui';
import { Command } from './Command';
import { IssueSection } from './IssueSection';
import { NewestFirstTitle } from './NewestFirstGlyph';
import {
  compactTokens,
  costPhrase,
  costTitle,
  durationPhrase,
  entryMark,
  entryTitle,
  errorPhrase,
  hasDescendantSpend,
  modelEffortPhrase,
  ownPhrase,
  playbookFileShowCommand,
  playbookSourcePhrase,
  readsLabel,
  resumeCommand,
  showsWorkLog,
  totalsPhrase,
  transitionPhrase,
} from '../lib/workLog';
import type { Status, WorkLog as WorkLogData, WorkLogEntry } from '../types';

/**
 * What this ticket cost: one row per agent session, its tokens and its notional
 * dollars, what it did in a sentence, and how long it took.
 *
 * Its own section, between the thread and the trail and visibly part of
 * neither. A comment is somebody talking, an event is something happening, and
 * this is the meter - three different kinds of fact, and reading them as one
 * list is how none of them get read.
 *
 * **Nothing here writes.** There is no control for adding, editing or deleting
 * an entry, and `api/client.ts` has no function that could: an entry is written
 * by the dispatcher with an API key and the server refuses a browser outright.
 * The absence is the guarantee - do not add one for symmetry.
 */
export function WorkLog({
  log,
  error,
  statuses,
}: {
  log: WorkLogData | null;
  error: string | null;
  statuses: Status[];
}) {
  // The section's own failure, said in the section. An issue somebody came here
  // to read should not blank because a meter could not be fetched.
  if (error) {
    return (
      <IssueSection id="work-log" title={<NewestFirstTitle>Work log</NewestFirstTitle>}>
        <p className="text-muted">{error}</p>
      </IssueSection>
    );
  }

  // Nothing at all, not an empty section: a heading over a blank space is a
  // page saying "this feature exists and has failed you".
  if (!showsWorkLog(log)) return null;

  const errors = errorPhrase(log.totals);

  return (
    <IssueSection id="work-log" title={<NewestFirstTitle>Work log</NewestFirstTitle>} count={log.entries.length}>
      {/* Tokens are the headline and the dollars are secondary, because on a
          subscription the dollars are notional list price rather than money
          that left an account. */}
      <p className="hatch-worklog-totals">{totalsPhrase(log.totals)}</p>

      {/* Said only when the two differ, so one number never passes for the
          other: an epic showing four sessions and one of its own is the whole
          feature, not a bug. */}
      {hasDescendantSpend(log) && <p className="text-muted">Everything beneath this issue, {ownPhrase(log)}.</p>}

      {errors && <p className="text-muted">{errors}.</p>}

      {log.entries.length === 0 ? (
        <p className="text-muted">No session has been run against this issue itself.</p>
      ) : (
        <ul className="hatch-worklog">
          {log.entries.map((entry) => (
            <Entry key={entry.id} entry={entry} statuses={statuses} />
          ))}
        </ul>
      )}
    </IssueSection>
  );
}

/** One session. Newest first is the server's ordering and is not re-derived. */
function Entry({ entry, statuses }: { entry: WorkLogEntry; statuses: Status[] }) {
  const mark = entryMark(entry);
  const resume = resumeCommand(entry.agent, entry.sessionId);
  const transition = transitionPhrase(entry, statuses);
  const modelEffort = modelEffortPhrase(entry);

  return (
    <li className={`hatch-worklog-entry${mark ? ` hatch-worklog-${mark}` : ''}`}>
      <div className="hatch-worklog-head">
        <strong className={entry.described ? undefined : 'text-muted'}>{entryTitle(entry)}</strong>
        {mark === 'error' && <Badge tone="danger">ended with an error</Badge>}
        {mark === 'undescribed' && <Badge>undescribed</Badge>}
      </div>

      {entry.summary && <p className="hatch-worklog-summary">{entry.summary}</p>}

      <div className="hatch-worklog-facts">
        {/* The headline first and the money after it, in that order every time
            it is drawn. */}
        <span>
          <strong>{compactTokens(entry.totalTokens)}</strong> tokens
        </span>
        <span className="text-muted" title={costTitle(entry.costUsd)}>
          {costPhrase(entry.costUsd)}
        </span>
        <span className="text-muted">{durationPhrase(entry.durationMs)}</span>
        <span className="text-muted">
          {entry.turns} {entry.turns === 1 ? 'turn' : 'turns'}
        </span>
        <span className="text-muted">{new Date(entry.endedAt).toLocaleString()}</span>
      </div>

      {playbookSourcePhrase(entry) && <p className="text-muted hatch-worklog-source">{playbookSourcePhrase(entry)}</p>}
      {transition && <p className="text-muted hatch-worklog-transition">{transition}</p>}
      {modelEffort && <p className="text-muted hatch-worklog-model">{modelEffort}</p>}

      {/* The session id in the form it is used in, rather than as an id
          somebody has to assemble a command around. */}
      <div className="hatch-worklog-commands">
        {resume ? <Command command={resume} /> : <code>{entry.sessionId}</code>}
        {entry.playbookSource === 'file' && entry.playbookFileSha && (
          <Command command={playbookFileShowCommand(entry.playbookFileSha)} />
        )}
      </div>

      {/* Folded by default - the native element's own state - and in the
          server's order, heaviest first. */}
      {entry.reads.length > 0 && (
        <details className="hatch-worklog-reads">
          <summary className="text-muted">{readsLabel(entry.reads)}</summary>
          <ul>
            {entry.reads.map((r) => (
              <li key={r.path}>
                <span className="hatch-worklog-reads__path">{r.path}</span>
                <span className="text-muted">×{r.count}</span>
              </li>
            ))}
          </ul>
        </details>
      )}
    </li>
  );
}
