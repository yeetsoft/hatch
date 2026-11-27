import { Link } from 'react-router-dom';
import { Card, EmptyState } from '@hatch/ui';
import {
  CHANGES_HEADING,
  FIRST_VISIT_NOTE,
  KIND_WORDS,
  NO_CHANGES,
  omittedWords,
  servesWords,
  sinceWords,
} from '../lib/ambitionChanges';
import { scopedPath } from '../lib/scope';
import type { AmbitionChangeLine, AmbitionChanges } from '../types';

/** What changed under the ambitions since the reader last looked. Drawn from
    props, so a test needs no read: `error`, or no `changes`, draws the failure
    sentence in the section's place and leaves the rest of the page alone. */
export function AmbitionChangesSection({
  changes,
  error,
  scopeKeys,
  now,
}: {
  changes: AmbitionChanges | null;
  error: string | null;
  scopeKeys: string[];
  now: Date;
}) {
  /* A first visit had no earlier look to be "ago": it says the week instead. */
  const firstVisit = changes?.basis === 'week';
  const ago = changes && !firstVisit ? sinceWords(changes.since, now) : null;

  return (
    <Card as="section" className="hatch-ambition-changes">
      <div className="hatch-ambition-changes-head">
        <h2 className="hatch-ambition-heading">{CHANGES_HEADING}</h2>
        {ago && <span className="text-muted">{ago}</span>}
      </div>

      {error || !changes ? (
        <p className="text-danger hatch-ambition-failure">
          What changed since you last looked could not be read: {error ?? 'no answer'}
        </p>
      ) : (
        <>
          {firstVisit && <p className="hatch-ambition-tile-note">{FIRST_VISIT_NOTE}</p>}
          {changes.lines.length === 0 ? (
            <EmptyState message={NO_CHANGES} />
          ) : (
            <ul className="hatch-ambition-changes-lines">
              {changes.lines.map((line, index) => (
                <li key={`${line.kind}-${line.issueKey}-${index}`}>
                  <ChangeLine line={line} scopeKeys={scopeKeys} />
                </li>
              ))}
              {changes.omitted > 0 && (
                <li className="hatch-ambition-changes-omitted">{omittedWords(changes.omitted)}</li>
              )}
            </ul>
          )}
        </>
      )}
    </Card>
  );
}

/** One line: its kind, then its words, the whole of it a link to the issue it
    is about - or, for a decision made without the reader, to the Ballot. */
function ChangeLine({ line, scopeKeys }: { line: AmbitionChangeLine; scopeKeys: string[] }) {
  const to = line.kind === 'decided' ? scopedPath('/ballot', scopeKeys) : `/issues/${line.issueKey}`;

  return (
    <Link to={to} className="hatch-ambition-changes-line">
      <span className="hatch-ambition-changes-kind">{KIND_WORDS[line.kind]}</span>
      <span className="hatch-ambition-changes-words">
        <LineWords line={line} />
      </span>
    </Link>
  );
}

function LineWords({ line }: { line: AmbitionChangeLine }) {
  const key = <span className="hatch-plan-key">{line.issueKey}</span>;

  switch (line.kind) {
    case 'holds':
      return (
        <>
          <span>{line.statement}</span>
          <span className="text-muted">{line.ambitionTitle}</span>
          {line.lookedAt && <span className="text-muted">Looked at: {line.lookedAt}</span>}
        </>
      );
    case 'carved':
      return (
        <>
          {key}
          <span>{line.issueTitle}</span>
          <span className="text-muted">{servesWords(line.serves)}</span>
        </>
      );
    case 'decided':
      return (
        <>
          {key}
          <span>{line.wentAheadOn ?? 'Nothing was named to go ahead on.'}</span>
        </>
      );
    case 'asked':
      return (
        <>
          {key}
          <span>{(line.count ?? 1) > 1 ? `${line.count} questions are waiting` : line.question}</span>
        </>
      );
  }
}
