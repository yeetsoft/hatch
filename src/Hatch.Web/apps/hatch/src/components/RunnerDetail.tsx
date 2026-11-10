import type { ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Badge, Button, Card, EmptyState, PageHeader, Text } from '@hatch/ui';
import { useRunnersOutlet } from './RunnersLayout';
import { lastUsedLabel, liveSessions, sessionsByStart } from '../lib/runners';
import { usePhone } from '../lib/viewport';
import { RecentSessions } from './RecentSessions';
import { SessionBlock } from './SessionBlock';
import type { Runner } from '../types';

/** The detail's two sessions cards - "Live sessions" and "Recent sessions" in
    docs/runners-page-design.md - shared between the phone and desk branches
    of `RunnerDetail` so they are written once. A retired runner draws no
    live blocks: its row's own badge already says so, and a replaced key's
    sessions are not this screen's to show as live. */
function SessionsCards({ runner, now }: { runner: Runner; now: Date }) {
  const live = Boolean(runner.retiredAt) ? [] : liveSessions(runner, now);
  const hosts = new Set(live.map((session) => session.host).filter((host): host is string => host !== null));

  return (
    <>
      <Card>
        <h2 className="hatch-section-title">Live sessions</h2>
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
              <SessionBlock key={session.id} session={session} runner={runner} now={now} />
            ))}
          </ul>
        )}
      </Card>

      <RecentSessions runner={runner} now={now} />
    </>
  );
}

/**
 * The detail's first card - "Identity and key" in
 * docs/runners-page-design.md. Identity facts draw as muted text, never
 * inputs; `rotatedAt`/`revokedAt` draw only when set.
 *
 * `actions` is a slot for the owner select, Rotate and Revoke buttons a later
 * task fills - nothing passes it yet. `refusal` draws inside this Card, never
 * at the page's top: a failed save belongs to the section whose control
 * failed, per "Identity and key".
 */
export function IdentityCard({ runner, actions, refusal }: { runner: Runner; actions?: ReactNode; refusal?: string | null }) {
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
      <Text tone="muted">{runner.for ? `for ${runner.for}` : 'no owner'}</Text>
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
  const { runners } = useRunnersOutlet();
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

  // Two badges, two fixes: Behind is a binary older than the board's
  // (restart it against a newer deploy), Rebuild one older than its own
  // checkout's source (make build-hatch there). Mirrors RunnersPage.tsx.
  const behind = runner.boardDrift === 'Behind';
  const rebuild = runner.sourceDrift === 'Behind';
  const actions = (behind || rebuild) && (
    <>
      {behind && <Badge tone="danger">Behind</Badge>}
      {rebuild && <Badge tone="danger">Rebuild</Badge>}
    </>
  );

  if (phone) {
    return (
      <>
        <Button as={Link} to="/runners">
          <span aria-hidden="true">‹</span> Runners
        </Button>
        <PageHeader title={name} actions={actions} level={1} />
        <IdentityCard runner={runner} />
        <SessionsCards runner={runner} now={now} />
      </>
    );
  }

  return (
    <>
      <PageHeader title={name} actions={actions} level={2} />
      <IdentityCard runner={runner} />
      <SessionsCards runner={runner} now={now} />
    </>
  );
}
