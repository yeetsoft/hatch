import { Button, Card } from '@hatch/ui';
import { resumeWords } from '../lib/attention';
import { markState } from '../lib/stall';
import type { AssigneeDirectory, Issue } from '../types';

/**
 * Beside `ClaimPanel`, and for the same stated reason: it is a fact about the
 * ticket that something else is or is not about to act on it, above
 * everything the page lets you change.
 *
 * One line, not a card with a heading - composed from `markState`, not drawn
 * as three branches here. Nothing at all where there is no mark: the
 * overwhelming majority of issues carry none, and this must cost them
 * nothing, the same argument `ClaimPanel.tsx` makes at length for a claim.
 *
 * Presentational, same as `ClaimPanel`: the busy flag, the error line and the
 * re-read are the page's. Two buttons, gated the way `ExpressControl` gates -
 * a key reading the page gets the words and no buttons, because an agent is
 * entitled to know why its ticket is sitting and a dead button is not how to
 * tell it.
 */
export function StallPanel({
  issue,
  heldBy,
  directory,
  busy,
  onResume,
  onHold,
}: {
  issue: Issue;
  /** Who last pressed hold, read off the event trail - null when nobody has. */
  heldBy: string | null;
  /** Everybody who could own an issue, and who the caller is. Null while it is still loading. */
  directory: AssigneeDirectory | null;
  /** A press is in flight. Both buttons stay where they are and stop answering. */
  busy: boolean;
  /** Resume now: PutHold({held: false}) - clears the mark whether or not it was held. */
  onResume: () => void;
  /** Hold for me: PutHold({held: true}). Offered only while stalled, never while already held. */
  onHold: () => void;
}) {
  const state = markState(issue);
  if (state === 'none') return null;

  const me = directory?.me ?? null;

  const line =
    state === 'held'
      ? `${issue.stalledWhy ? `${issue.stalledWhy} - ` : ''}Held${heldBy ? ` by ${heldBy}` : ''}`
      : `Stalled${issue.stalledWhy ? ` - ${issue.stalledWhy}` : ''} - resumes ${resumeWords(issue.stalledAt!, issue.stallResumeSeconds, new Date())}`;

  return (
    <Card className="hatch-stall">
      <p className="hatch-stall-line">{line}</p>

      {me?.kind === 'person' ? (
        <div className="hatch-form-actions">
          <Button disabled={busy} onClick={onResume}>
            Resume now
          </Button>
          {state === 'stalled' && (
            <Button disabled={busy} onClick={onHold}>
              Hold for me
            </Button>
          )}
        </div>
      ) : (
        <span className="hatch-stall-said">{state === 'held' ? 'Held' : 'Stalled'}</span>
      )}
    </Card>
  );
}
