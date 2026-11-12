import { useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Badge, Button, Card, EmptyState, Modal, PageHeader, Text } from '@hatch/ui';
import {
  orderRunnerSession,
  patchRunner,
  patchRunnerSession,
  pauseAllSessions,
  putRunnerRefusedModels,
  stopAllSessions,
  withdrawSessionOrder,
} from '../api/client';
import { useRunnersOutlet } from './RunnersLayout';
import { RunnerBoundsForm } from './RunnerBoundsForm';
import { RunnerKeyActions } from './RunnerKeyActions';
import { RunnerSettingsForm } from './RunnerSettingsForm';
import { SecretPanel } from './SecretPanel';
import {
  isRetired,
  lastUsedLabel,
  liveLoopSessions,
  liveSessions,
  runnerActionable,
  sessionsByStart,
} from '../lib/runners';
import { saveRunner, saveRunnerSettings } from '../lib/saveRunner';
import { sessionActions } from '../lib/sessionActions';
import { useMe } from '../lib/useMe';
import { usePhone } from '../lib/viewport';
import { RecentSessions } from './RecentSessions';
import { SessionBlock } from './SessionBlock';
import type { Runner, RunnerPatchRequest, RunnerSession } from '../types';

/** The detail's two sessions cards - "Live sessions" and "Recent sessions" in
    docs/runners-page-design.md - shared between the phone and desk branches
    of `RunnerDetail` so they are written once. A retired runner draws no
    live blocks: its row's own badge already says so, and a replaced key's
    sessions are not this screen's to show as live. */
function SessionsCards({
  runner,
  live,
  now,
  reload,
}: {
  runner: Runner;
  live: RunnerSession[];
  now: Date;
  reload: () => Promise<void>;
}) {
  const [refusal, setRefusal] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const hosts = new Set(live.map((session) => session.host).filter((host): host is string => host !== null));
  const actions = sessionActions(
    runner.name,
    { patchRunnerSession, orderRunnerSession, withdrawSessionOrder, pauseAllSessions, stopAllSessions },
    reload,
  );
  const loops = liveLoopSessions(live).length;
  const fanOut = runner.canControl && runnerActionable(runner) && !isRetired(runner) && loops > 0;

  const stopAll = async () => {
    setBusy(true);
    try {
      setRefusal(await actions.stopAll());
    } finally {
      setBusy(false);
      setConfirming(false);
    }
  };

  return (
    <>
      <Card>
        <div className="hatch-section-head">
          <h2 className="hatch-section-title">Live sessions</h2>
          {fanOut && (
            <div className="hatch-section-actions">
              <Button onClick={async () => setRefusal(await actions.pauseAll())}>Pause all</Button>
              <Button variant="danger" onClick={() => setConfirming(true)}>
                Stop all
              </Button>
            </div>
          )}
        </div>
        {!runner.canControl && live.length > 0 && <Text tone="muted">{`${runner.for ?? 'its owner'}'s to control`}</Text>}
        {refusal && <p className="text-danger">{refusal}</p>}
        {hosts.size >= 2 && (
          <p className="hatch-runner-note">
            This key is in use from {hosts.size} machines. Tags and bounds are per runner, so a key is meant to be
            one machine.
          </p>
        )}
        {live.length === 0 ? (
          <Text tone="muted">
            No live sessions. Start one with <code>hatch go-to-work</code> in a checkout set up with this key.
          </Text>
        ) : (
          <ul className="hatch-runner-sessions">
            {sessionsByStart(live).map((session) => (
              <SessionBlock
                key={session.id}
                session={session}
                runner={runner}
                now={now}
                actions={actions}
                onRefusal={setRefusal}
              />
            ))}
          </ul>
        )}
      </Card>

      <Modal
        open={confirming}
        onClose={() => setConfirming(false)}
        title={`Stop ${loops} sessions after their current increment?`}
        footer={
          <div className="hatch-form-actions">
            <Button onClick={() => setConfirming(false)}>Leave it</Button>
            <Button variant="danger" loading={busy} onClick={stopAll}>
              Stop all
            </Button>
          </div>
        }
      >
        <Text>Each finishes its current increment, then stops. None is cut off partway.</Text>
      </Modal>

      <RecentSessions runner={runner} now={now} />
    </>
  );
}

/**
 * The detail's two editable cards - "What it may work" and "Bounds" in
 * docs/runners-page-design.md - each with its own save and its own refusal.
 *
 * Its own component because `RunnerDetail` returns early, before a hook could
 * go. One refusal per card, and a save sets only its own, so one card's save
 * never clears what the other is showing. The forms are keyed by runner so a
 * draft never carries over from one runner to the next.
 *
 * Editable is `canControl` - already true for an admin and for a wall that is
 * off - on a runner that is neither revoked nor retired.
 */
function RunnerConfigCards({
  runner,
  live,
  reload,
}: {
  runner: Runner;
  live: RunnerSession[];
  reload: () => Promise<void>;
}) {
  const [settingsRefusal, setSettingsRefusal] = useState<string | null>(null);
  const [boundsRefusal, setBoundsRefusal] = useState<string | null>(null);
  const editable = runner.canControl && runnerActionable(runner) && !isRetired(runner);

  const save = (request: RunnerPatchRequest) => saveRunner(runner.name, request, { patch: patchRunner, reload });

  return (
    <>
      <RunnerSettingsForm
        key={`settings:${runner.name}`}
        runner={runner}
        live={live}
        editable={editable}
        refusal={settingsRefusal}
        onSave={async (request) =>
          setSettingsRefusal(
            await saveRunnerSettings(runner.name, request, runner.refusedModels, runner.agent, {
              patch: patchRunner,
              putRefused: putRunnerRefusedModels,
              reload,
            }),
          )
        }
      />
      <RunnerBoundsForm
        key={`bounds:${runner.name}`}
        runner={runner}
        live={live}
        editable={editable}
        refusal={boundsRefusal}
        onSave={async (request) => setBoundsRefusal(await save(request))}
      />
    </>
  );
}

/**
 * The detail's first card - "Identity and key" in
 * docs/runners-page-design.md. Identity facts draw as muted text, never
 * inputs; `rotatedAt`/`revokedAt` draw only when set.
 *
 * `actions` is a slot for the owner select, Rotate and Revoke buttons -
 * `RunnerKeyActions`, for an admin on a runner that is neither revoked nor
 * retired. `refusal` draws inside this Card, never at the page's top: a
 * failed save belongs to the section whose control failed, per "Identity
 * and key".
 *
 * `ownerEditable` hides the "for X / no owner" text line, since
 * `RunnerKeyActions`'s own owner select already says who it is for - an
 * Owner or a Viewer still reads it as text.
 */
export function IdentityCard({
  runner,
  actions,
  refusal,
  ownerEditable,
}: {
  runner: Runner;
  actions?: ReactNode;
  refusal?: string | null;
  ownerEditable?: boolean;
}) {
  return (
    <Card>
      <h2 className="hatch-section-title">Identity and key</h2>
      <Text>{runner.name}</Text>
      {runner.prefix != null && (
        <Text tone="muted">
          <code>{runner.prefix}</code>
        </Text>
      )}
      {runner.createdAt != null && <Text tone="muted">Created {new Date(runner.createdAt).toLocaleString()}</Text>}
      <Text tone="muted">Last used {lastUsedLabel(runner.lastUsedAt ?? null)}</Text>
      {runner.rotatedAt != null && <Text tone="muted">Rotated {new Date(runner.rotatedAt).toLocaleString()}</Text>}
      {runner.revokedAt != null && <Text tone="muted">Revoked {new Date(runner.revokedAt).toLocaleString()}</Text>}
      {!ownerEditable && <Text tone="muted">{runner.for ? `for ${runner.for}` : 'no owner'}</Text>}
      {refusal && <p className="text-danger">{refusal}</p>}
      {actions}
    </Card>
  );
}

/** The right pane's `:name` route: the runner's own header and its identity
    card, or a not-found state. Reads the runner out of the layout's own poll,
    with no second fetch - see docs/runners-page-design.md, "Routes and
    layout". */
export function RunnerDetail() {
  const { name } = useParams();
  const { runners, reload, secret, setSecret } = useRunnersOutlet();
  const { isAdmin } = useMe();
  const phone = usePhone();
  const now = new Date();

  if (runners === null) return null;

  const runner = runners.find((r) => r.name === name);

  if (!runner) {
    return (
      <EmptyState
        message={`No runner called ${name}.`}
        action={
          <Button as={Link} to="/runners">
            Runners
          </Button>
        }
      />
    );
  }

  // A retired runner draws no live blocks: its row's own badge already says
  // so, and a replaced key's sessions are not this screen's to show as live.
  const live = isRetired(runner) ? [] : liveSessions(runner, now);

  // Two badges, two fixes: Behind is a binary older than the board's
  // (restart it against a newer deploy), Rebuild one older than its own
  // checkout's source (make build-hatch there).
  const behind = runner.boardDrift === 'Behind';
  const rebuild = runner.sourceDrift === 'Behind';
  const badges = (behind || rebuild) && (
    <>
      {behind && <Badge tone="danger">Behind</Badge>}
      {rebuild && <Badge tone="danger">Rebuild</Badge>}
    </>
  );

  // Admin, on a runner that is neither revoked nor retired - "Who sees what":
  // only that variant creates, picks an owner, rotates or revokes.
  const canManageKey = isAdmin && runnerActionable(runner) && !isRetired(runner);
  const keyActions = canManageKey && (
    <RunnerKeyActions runner={runner} onSecret={setSecret} reload={reload} />
  );
  const secretPanel = secret?.name === runner.name && <SecretPanel secret={secret} onDone={() => setSecret(null)} />;

  if (phone) {
    return (
      <>
        <Button as={Link} to="/runners">
          <span aria-hidden="true">‹</span> Runners
        </Button>
        <PageHeader title={name} actions={badges} level={1} />
        {secretPanel}
        <IdentityCard runner={runner} actions={keyActions} ownerEditable={canManageKey} />
        <RunnerConfigCards runner={runner} live={live} reload={reload} />
        <SessionsCards runner={runner} live={live} now={now} reload={reload} />
      </>
    );
  }

  return (
    <>
      <PageHeader title={name} actions={badges} level={2} />
      {secretPanel}
      <IdentityCard runner={runner} actions={keyActions} ownerEditable={canManageKey} />
      <RunnerConfigCards runner={runner} live={live} reload={reload} />
      <SessionsCards runner={runner} live={live} now={now} reload={reload} />
    </>
  );
}
