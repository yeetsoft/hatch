import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@hatch/ui';
import { evidenceLines, findingTimeWords } from '../lib/goose';
import { FindingBugDialog } from './FindingBugDialog';
import { GooseFindingBug } from './GooseFindingBug';
import { GooseRow } from './GooseRow';
import type { GooseFinding as Finding } from '../types';

/**
 * One finding, open or closed: what is wrong, its check, its project and how
 * long, with its evidence and the bug it became under it once opened.
 *
 * The File a bug button is the attention panel's own - the same dialog - and
 * sits beside the row's toggle rather than inside it. Once a bug is filed the
 * key stands in its place, linked to the issue.
 */
export function GooseFinding({
  finding: f,
  now,
  refreshed,
  onFiled,
}: {
  finding: Finding;
  now: Date;
  /** Changes when the page reads again; passed on to the bug's own reads. */
  refreshed: unknown;
  onFiled: () => void;
}) {
  const [dialog, setDialog] = useState(false);
  const evidence = evidenceLines(f);

  return (
    <GooseRow
      head={
        <>
          <span className="hatch-goose-title">{f.sentence}</span>
          <span className="hatch-goose-meta">
            {[f.check, f.projectKey, findingTimeWords(f, now)].filter((w) => w).join(' · ')}
          </span>
        </>
      }
      actions={
        f.bugIssueKey ? (
          <Link to={`/issues/${f.bugIssueKey}`}>{f.bugIssueKey}</Link>
        ) : (
          <>
            <Button variant="primary" onClick={() => setDialog(true)}>
              File a bug
            </Button>
            <FindingBugDialog
              open={dialog}
              findingId={f.id}
              projectKey={f.projectKey}
              onClose={() => setDialog(false)}
              onFiled={onFiled}
            />
          </>
        )
      }
    >
      {evidence.length === 0 ? (
        <p className="hatch-goose-note">The check recorded no evidence.</p>
      ) : (
        <dl className="hatch-goose-pairs">
          {evidence.map((e, i) => (
            <div key={`${i}-${e.label}`} className="hatch-goose-pair">
              <dt>{e.label}</dt>
              <dd>{e.value}</dd>
            </div>
          ))}
        </dl>
      )}

      {f.bugIssueKey && <GooseFindingBug bugKey={f.bugIssueKey} refreshed={refreshed} now={now} />}
    </GooseRow>
  );
}
