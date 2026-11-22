import type { WaitingPullRequest as Waiting } from '../types';

/**
 * Whether a waiting pull request is to be drawn: one exists, and none is
 * recorded. The API clears it in the same save that records a url, so both at
 * once is a stale read, and the url wins.
 */
function shown(url: string | null, waiting: Waiting | null | undefined): waiting is Waiting {
  return !url && !!waiting;
}

/**
 * The chip beside where the pull request link would be: a pull request has
 * been written and nobody has opened it yet. Nothing at all otherwise, the
 * way PullRequestLink draws nothing for an issue that has none.
 */
export function WaitingPullRequestChip({
  url,
  waiting,
}: {
  url: string | null;
  waiting: Waiting | null | undefined;
}) {
  if (!shown(url, waiting)) return null;

  return (
    <span className="hatch-chip" title={`Written on ${waiting.branch}, waiting to be opened`}>
      <span className="hatch-chip-kind">Pull request</span>
      waiting
    </span>
  );
}

/**
 * The issue facts' Pull request field when none is recorded but one is
 * waiting: who wrote it, why it is not open, when, and the title and body to
 * read. Read-only - a person records the url as they do today, and that
 * clears this. The dash in its place when there is nothing waiting.
 */
export function WaitingPullRequestFact({
  url,
  waiting,
}: {
  url: string | null;
  waiting: Waiting | null | undefined;
}) {
  if (!shown(url, waiting)) return <span className="text-muted">—</span>;

  return (
    <span className="hatch-waiting-pr">
      <span>Written, waiting to be opened</span>
      <span className="text-muted">
        {' '}
        by {waiting.runner} on {new Date(waiting.at).toLocaleString()}: {waiting.why}
      </span>
      <details>
        <summary>{waiting.title}</summary>
        <p className="text-muted">
          Branch {waiting.branch} at {waiting.sha.slice(0, 12)}
          {waiting.remote ? ` in ${waiting.remote}` : ''}
        </p>
        <pre className="hatch-waiting-pr__body">{waiting.body}</pre>
      </details>
    </span>
  );
}
