import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@hatch/ui';
import { message } from '../lib/errors';
import { applyGoToWork } from '../lib/goToWork';
import type { GoToWorkPlan, StatusLine } from '../lib/goToWork';
import { GoToWorkStatus } from './GoToWorkStatus';

/**
 * What the button cannot do, said beside it: the way out of the one mode it is
 * disabled in for a reason a person can act on (the status line says the
 * reason itself). Null for every other mode. Exported so the board can draw it
 * under the bar on a phone, where a note inside the bar would push New off it.
 */
export function GoToWorkNote({ plan }: { plan: GoToWorkPlan }) {
  if (plan.mode === 'none') return <Link to="/docs/the-runner#starting-it-at-login">Start one at login</Link>;
  return null;
}

/**
 * One press to start or pause the viewer's own loops - the board's header
 * button, built on `planGoToWork`'s verdict, with the status line beside it on
 * desktop. The plan, the line and the page's single runners read are the
 * board's; this owns only the press in flight.
 *
 * A press shows its result at once (go reads Pause, pause reads Go to Work)
 * and the optimistic mode is dropped when the re-read lands, so the label never
 * waits on the network and never stays wrong after it. The project is the bound
 * on a start only, and a picker change after a press writes nothing.
 */
export function GoToWorkControl({
  plan,
  status,
  project,
  reload,
  isPhone,
}: {
  plan: GoToWorkPlan;
  status: StatusLine;
  project: string;
  reload: () => Promise<void>;
  isPhone: boolean;
}) {
  const [optimistic, setOptimistic] = useState<'go' | 'pause' | null>(null);
  /* Keyed to the plan it failed against: a new plan is a new read of the runners,
     and an old refusal says nothing about it. */
  const [failure, setFailure] = useState<{ plan: GoToWorkPlan; text: string } | null>(null);
  const error = failure?.plan === plan ? failure.text : null;

  const mode = optimistic ?? plan.mode;
  const pausing = mode === 'pause';

  const press = async () => {
    setFailure(null);
    if (plan.mode === 'go') setOptimistic('pause');
    else if (plan.mode === 'pause') setOptimistic('go');
    try {
      await applyGoToWork(plan, project);
      await reload();
    } catch (err) {
      setFailure({ plan, text: message(err) });
    } finally {
      setOptimistic(null);
    }
  };

  const disabled = optimistic !== null || mode === 'loading' || mode === 'none' || mode === 'stopping';
  const words = pausing ? 'Pause' : 'Go to Work';

  return (
    <span className="hatch-go-to-work">
      <Button
        variant="success"
        disabled={disabled}
        aria-label={words}
        title={
          pausing
            ? 'Pause your runners - each finishes the ticket in hand first'
            : `Start your runners on ${project || 'every project'}`
        }
        onClick={() => void press()}
      >
        {isPhone && !pausing ? 'Go' : words}
      </Button>
      {error && (
        <span role="alert" className="hatch-go-to-work-error text-danger">
          {error}
        </span>
      )}
      {!isPhone && <GoToWorkStatus status={status} />}
      {!isPhone && plan.mode === 'none' && (
        <span className="hatch-go-to-work-note text-muted">
          <GoToWorkNote plan={plan} />
        </span>
      )}
    </span>
  );
}
