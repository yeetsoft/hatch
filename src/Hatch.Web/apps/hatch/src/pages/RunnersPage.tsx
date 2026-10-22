import { Fragment, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Badge, Button, Card, EmptyState, Field, PageHeader, Table, Text } from '@hatch/ui';
import { orderRunner, patchRunner, withdrawOrder } from '../api/client';
import { MomentField } from '../components/MomentField';
import { agoPhrase } from '../lib/claim';
import { message } from '../lib/errors';
import {
  ACTIVITY_TONES,
  activityParts,
  boundsChanged,
  boundsOf,
  boundsProblem,
  boundsRequest,
  drivesLabel,
  isControllable,
  isIssueKeyShape,
  MAX_SPEND_HINT,
  mineLabel,
  orderSentence,
  repositoriesLabel,
  runnerActivity,
  tagsLabel,
  type RunnerBounds,
} from '../lib/runners';
import { useRunners } from '../lib/useRunners';
import type { Runner, RunnerOrderRequest, RunnerPatchRequest } from '../types';

/**
 * Every runner that has spoken to this Hatch lately, and the three things a
 * person can tell one: keep going, hold, or finish and stop.
 *
 * Nothing on this page starts a process, and that is the design rather than a
 * limitation. A press writes down what the board would like; the runner asks
 * for it between increments and obeys - which is what makes it work for a loop
 * in a container and for one on somebody's laptop behind a router nothing can
 * reach. The cost is that a press takes effect at the top of the next pass,
 * after whatever increment is in flight has finished, and the page says so
 * rather than pretending otherwise.
 */
export function RunnersPage() {
  const { runners, error, reload } = useRunners();
  const [failure, setFailure] = useState<string | null>(null);

  async function act(action: () => Promise<unknown>) {
    try {
      await action();
      setFailure(null);
    } catch (err) {
      setFailure(message(err));
    }

    // Whatever happened, ask again: a refused press must not leave the row
    // drawing what was asked for rather than what is true.
    await reload();
  }

  // One instant for the whole page, so two rows a second apart are not judged
  // against two different nows.
  const now = new Date();

  return (
    <div className="hatch-page">
      <PageHeader
        title="Runners"
        description="The loops that have spoken to this Hatch lately. A runner asks for its instructions between increments, so anything set here takes effect on its next pass - never in the middle of one."
      />

      {(failure ?? error) && <p className="text-danger">{failure ?? error}</p>}

      {runners?.length === 0 && (
        <EmptyState message="Nothing has run here lately. A runner appears within a minute of hatch go-to-work starting, and drops off two hours after it stops." />
      )}

      {runners && runners.length > 0 && (
        <Card flush>
          <Table>
            <thead>
              <tr>
                <th>Runner</th>
                <th>Doing</th>
                <th>Last said</th>
                <th>Heard from</th>
                <th>Bounds</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {runners.map((runner) => (
                <Row
                  key={runner.name}
                  runner={runner}
                  now={now}
                  onPatch={(request) => void act(() => patchRunner(runner.name, request))}
                  onOrder={(request) => void act(() => orderRunner(runner.name, request))}
                  onWithdraw={() => void act(() => withdrawOrder(runner.name))}
                />
              ))}
            </tbody>
          </Table>
        </Card>
      )}
    </div>
  );
}

export function Row({
  runner,
  now,
  onPatch,
  onOrder,
  onWithdraw,
}: {
  runner: Runner;
  now: Date;
  onPatch: (request: RunnerPatchRequest) => void;
  onOrder: (request: RunnerOrderRequest) => void;
  onWithdraw: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [key, setKey] = useState('');
  const activity = runnerActivity(runner, now);
  const controllable = isControllable(runner);
  const ownedByAnother = runner.kind === 'loop' && !runner.canControl;
  const { words, claimKey } = activityParts(runner, now);

  return (
    <>
      <tr>
        <td>
          <strong className="hatch-runner-name">{runner.name}</strong>
          {/* Said out loud rather than left to the absent controls, because a
              row with no buttons reads as a bug otherwise. */}
          <Text tone="muted">{runner.kind === 'loop' ? 'loop' : 'one increment'}</Text>
          {/* The machine and checkout, under the name now that the name is a
              character and not a path - absent from an older CLI's row. */}
          {runner.where && <Text tone="muted">{runner.where}</Text>}
          {runner.agent && <Text tone="muted">{runner.agent}</Text>}
          <Text tone="muted">{drivesLabel(runner)}</Text>
        </td>
        <td>
          <Badge tone={ACTIVITY_TONES[activity]}>{words}</Badge>
          {claimKey && (
            <Link className="hatch-card-key" to={`/issues/${claimKey}`}>
              {claimKey}
            </Link>
          )}
        </td>
        <td>{runner.line ?? <Text tone="muted">nothing yet</Text>}</td>
        <td>{agoPhrase(runner.lastSeenAt, now)}</td>
        <td>
          <BoundsSummary runner={runner} />
        </td>
        <td>
          {controllable && (
            <div className="hatch-reorder hatch-reorder--wrap">
              <Button
                onClick={() => onPatch({ state: runner.state === 'paused' ? 'running' : 'paused' })}
                disabled={runner.state === 'stopping'}
              >
                {runner.state === 'paused' ? 'Resume' : 'Pause'}
              </Button>
              <Button
                variant="danger"
                onClick={() => onPatch({ state: 'stopping' })}
                disabled={runner.state === 'stopping'}
              >
                Stop after this one
              </Button>
              <Button onClick={() => setOpen(!open)}>{open ? 'Hide bounds' : 'Bounds'}</Button>
              {runner.order ? (
                <>
                  <Text tone="muted">{orderSentence(runner.order, now)}</Text>
                  <Button onClick={onWithdraw}>Withdraw</Button>
                </>
              ) : (
                <>
                  <input
                    aria-label="Ticket to work"
                    placeholder="HA-12"
                    value={key}
                    disabled={runner.state === 'stopping'}
                    onChange={(e) => setKey(e.target.value)}
                  />
                  <Button
                    disabled={runner.state === 'stopping' || !isIssueKeyShape(key)}
                    onClick={() => {
                      onOrder({ kind: 'work', key: key.trim() });
                      setKey('');
                    }}
                  >
                    Work this ticket
                  </Button>
                  <Button disabled={runner.state === 'stopping'} onClick={() => onOrder({ kind: 'pass' })}>
                    Run one pass
                  </Button>
                </>
              )}
            </div>
          )}
          {ownedByAnother && <Text tone="muted">{`${runner.for ?? 'its owner'}'s to control`}</Text>}
        </td>
      </tr>
      {open && (
        <tr>
          <td colSpan={6}>
            <BoundsForm runner={runner} onSave={onPatch} />
          </td>
        </tr>
      )}
    </>
  );
}

/** The bounds at a glance: only the ones that are set, because four "no cap"s
    on every row would be four things to read past to find the one that is
    holding. */
export function BoundsSummary({ runner }: { runner: Runner }) {
  const said: ReactNode[] = [
    mineLabel(runner),
    runner.under && (
      <>
        under{' '}
        <Link className="hatch-card-key" to={`/issues/${runner.under}`}>
          {runner.under}
        </Link>
      </>
    ),
    repositoriesLabel(runner),
    tagsLabel(runner),
    runner.maxRuns !== null && `${runner.maxRuns} run(s)`,
    runner.maxSpend !== null && `$${runner.maxSpend}`,
    runner.untilAt && `until ${new Date(runner.untilAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`,
  ].filter(Boolean);

  return said.length === 0 ? (
    <Text tone="muted">no bounds</Text>
  ) : (
    <>
      {said.map((part, i) => (
        <Fragment key={i}>
          {i > 0 && ', '}
          {part}
        </Fragment>
      ))}
    </>
  );
}

/**
 * The four bounds, edited together.
 *
 * Saved on a button rather than on blur, for the reason the playbook prompt is:
 * these are numbers somebody is part way through typing, and a PATCH fired
 * because they tabbed out of the box would set a budget of "4" on the way to
 * "40".
 */
function BoundsForm({ runner, onSave }: { runner: Runner; onSave: (request: RunnerPatchRequest) => void }) {
  const [draft, setDraft] = useState<RunnerBounds>(boundsOf(runner));
  const dirty = boundsChanged(draft, runner);
  const problem = boundsProblem(draft);

  const set = (field: keyof RunnerBounds, value: string) => setDraft({ ...draft, [field]: value });

  return (
    <div className="hatch-runner-bounds">
      <div className="hatch-field-grid">
        <Field label="Under" hint="An epic to stay inside, like AER-930. Empty is the whole board.">
          <input value={draft.under} onChange={(e) => set('under', e.target.value)} />
        </Field>
        <Field label="Max runs" hint="Increments this night may spend, counting the ones already spent. Empty is no cap.">
          <input inputMode="numeric" value={draft.maxRuns} onChange={(e) => set('maxRuns', e.target.value)} />
        </Field>
        <Field label="Max spend" hint={MAX_SPEND_HINT}>
          <input inputMode="decimal" value={draft.maxSpend} onChange={(e) => set('maxSpend', e.target.value)} />
        </Field>
        <MomentField
          label="Until"
          hint="An hour to stop by, in this browser's timezone."
          value={draft.untilAt === '' ? null : draft.untilAt}
          onChange={(next) => set('untilAt', next)}
        />
      </div>

      {problem && <p className="text-danger">{problem}</p>}

      <div className="hatch-form-actions">
        <Button
          variant="primary"
          disabled={!dirty || problem !== null}
          onClick={() => onSave(boundsRequest(draft))}
        >
          Save bounds
        </Button>
        <Button disabled={!dirty} onClick={() => setDraft(boundsOf(runner))}>
          Revert
        </Button>
      </div>

      <Text tone="muted">
        Folded into the loop at its next heartbeat, and applied from its next ticket onward. A cap set below what
        this night has already spent stops it at the next pass.
      </Text>
    </div>
  );
}
