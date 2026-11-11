import { useState } from 'react';
import { Outlet, useMatch, useNavigate, useOutletContext } from 'react-router-dom';
import { Button, EmptyState, PageHeader } from '@hatch/ui';
import { CreateRunnerDialog } from './CreateRunnerDialog';
import { RunnerList } from './RunnerList';
import { useRunners } from '../lib/useRunners';
import { useMe } from '../lib/useMe';
import { usePhone } from '../lib/viewport';
import type { RunnerSecret } from '../lib/runnerKey';
import type { Runner } from '../types';

export type { RunnerSecret } from '../lib/runnerKey';

/** What the layout hands its detail route through the `<Outlet/>` context. */
export interface RunnersOutlet {
  runners: Runner[] | null;
  secret: RunnerSecret | null;
  setSecret: (secret: RunnerSecret | null) => void;
  /** Ask the poll again now - a save asks, so the card draws what is true. */
  reload: () => Promise<void>;
}

export const useRunnersOutlet = () => useOutletContext<RunnersOutlet>();

/** The right pane's `index` route: nothing is picked yet. */
export function RunnersIndex() {
  return <EmptyState message="Pick a runner to see its sessions and settings." />;
}

/**
 * The master-detail shell at `/runners`: one `useRunners` poll shared by the
 * list and the detail, a grid on the desk and two pages on the phone - see
 * docs/runners-page-design.md, "Routes and layout".
 */
export function RunnersLayout() {
  const { runners, error, reload } = useRunners(true, true);
  const { me, isAdmin } = useMe();
  const wallOff = me?.kind === 'local' || me?.kind === 'none';
  const now = new Date();
  const phone = usePhone();
  const navigate = useNavigate();
  const [secret, setSecret] = useState<RunnerSecret | null>(null);
  const [creating, setCreating] = useState(false);
  const outlet = { runners, secret, setSecret, reload };

  const match = useMatch('/runners/:name');
  const selected = match?.params.name;

  function onCreated(created: RunnerSecret) {
    setCreating(false);
    setSecret(created);
    navigate(`/runners/${encodeURIComponent(created.name)}`);
    void reload();
  }

  const list = (
    <RunnerList
      runners={runners}
      selected={selected}
      isAdmin={isAdmin}
      wallOff={wallOff}
      now={now}
      onCreate={() => setCreating(true)}
    />
  );
  // Never on the wall-off variant: the admin routes this opens are dormant
  // there, and hatch config bootstraps the runner instead.
  const headerActions = isAdmin && (
    <Button variant="primary" onClick={() => setCreating(true)}>
      Create runner
    </Button>
  );
  const header = <PageHeader title="Runners" actions={headerActions} />;
  const failed = error && <p className="text-danger">{error}</p>;
  const dialog = <CreateRunnerDialog open={creating} onClose={() => setCreating(false)} onCreated={onCreated} />;

  if (phone) {
    if (match) {
      return (
        <div className="hatch-page">
          <Outlet context={outlet} />
        </div>
      );
    }

    return (
      <div className="hatch-page">
        {header}
        {failed}
        {list}
        {dialog}
      </div>
    );
  }

  return (
    <div className="hatch-page hatch-page--split">
      {header}
      {failed}
      <div className="hatch-runners-split">
        <section className="hatch-runners-pane">{list}</section>
        <section className="hatch-runners-pane">
          <Outlet context={outlet} />
        </section>
      </div>
      {dialog}
    </div>
  );
}
