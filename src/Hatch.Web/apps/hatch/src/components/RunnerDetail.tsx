import type { ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Badge, Button, Card, EmptyState, PageHeader, Text } from '@hatch/ui';
import { useRunnersOutlet } from './RunnersLayout';
import { lastUsedLabel } from '../lib/runners';
import { usePhone } from '../lib/viewport';
import type { Runner } from '../types';

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
      </>
    );
  }

  return (
    <>
      <PageHeader title={name} actions={actions} level={2} />
      <IdentityCard runner={runner} />
    </>
  );
}
