import { Button, Card, EmptyState } from '@hatch/ui';
import { RunnerRow } from './RunnerRow';
import { sortRunners } from '../lib/runners';
import type { Runner } from '../types';

export interface RunnerListProps {
  /** Null while the read is out: nothing is drawn, so loading is never mistaken for empty. */
  runners: Runner[] | null;
  /** The decoded name of the runner whose detail is open, if any. */
  selected?: string;
  isAdmin: boolean;
  /** Whether the wall is off, where nobody is admin and a checkout makes its own runner. */
  wallOff: boolean;
  now: Date;
  onCreate?: () => void;
}

/** The sentence an empty list says, by who is reading it. Wall off is asked
    first, rather than leaning on nobody being admin there. */
function emptyState(wallOff: boolean, isAdmin: boolean, onCreate?: () => void) {
  if (wallOff) {
    return (
      <EmptyState
        message={
          <>
            No runners yet. Run <code>hatch config</code> in a checkout and it makes its own.
          </>
        }
      />
    );
  }

  if (isAdmin) {
    return (
      <EmptyState
        message="No runners yet. A runner is a key: create one, then hand its secret to a checkout."
        action={
          <Button variant="primary" onClick={onCreate}>
            Create runner
          </Button>
        }
      />
    );
  }

  return <EmptyState message="No runners yet. Ask an admin to create one." />;
}

/**
 * The master pane: live runners first, then the rest, and revoked ones in a
 * group of their own, collapsed unless the open runner is among them so its
 * row is never hidden. The order is `sortRunners`'s, done here at render.
 */
export function RunnerList({ runners, selected, isAdmin, wallOff, now, onCreate }: RunnerListProps) {
  if (runners === null) return null;

  const { shown, revoked } = sortRunners(runners, now);
  if (shown.length === 0 && revoked.length === 0) return emptyState(wallOff, isAdmin, onCreate);

  const row = (runner: Runner) => (
    <li key={runner.name}>
      <RunnerRow runner={runner} selected={runner.name === selected} now={now} />
    </li>
  );

  return (
    <Card flush>
      {shown.length > 0 && <ul className="hatch-runner-list">{shown.map(row)}</ul>}
      {revoked.length > 0 && (
        <details className="hatch-runner-revoked" open={revoked.some((runner) => runner.name === selected)}>
          <summary>Revoked ({revoked.length})</summary>
          <ul className="hatch-runner-list">{revoked.map(row)}</ul>
        </details>
      )}
    </Card>
  );
}
