import { useState } from 'react';
import { Outlet, useMatch, useOutletContext } from 'react-router-dom';
import { EmptyState, PageHeader } from '@hatch/ui';
import { RunnerList } from './RunnerList';
import { useRunners } from '../lib/useRunners';
import { useMe } from '../lib/useMe';
import { usePhone } from '../lib/viewport';
import type { Runner } from '../types';

/** A newly-minted or rotated secret, shown once - "The secret panel" in
    docs/runners-page-design.md. Held only in this `useState`, never storage,
    URL or router state, so it is gone the moment the page is left. Nothing
    renders it yet; a later task draws `SecretPanel` from it. */
export interface RunnerSecret {
  name: string;
  secret: string;
  rotated: boolean;
}

/** What the layout hands its detail route through the `<Outlet/>` context. */
export interface RunnersOutlet {
  runners: Runner[] | null;
  secret: RunnerSecret | null;
  setSecret: (secret: RunnerSecret | null) => void;
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
  const { runners, error } = useRunners(true, true);
  const { me, isAdmin } = useMe();
  const wallOff = me?.kind === 'local' || me?.kind === 'none';
  const now = new Date();
  const phone = usePhone();
  const [secret, setSecret] = useState<RunnerSecret | null>(null);
  const outlet = { runners, secret, setSecret };

  const match = useMatch('/runners/:name');
  const selected = match?.params.name;

  const list = <RunnerList runners={runners} selected={selected} isAdmin={isAdmin} wallOff={wallOff} now={now} />;
  const header = <PageHeader title="Runners" />;
  const failed = error && <p className="text-danger">{error}</p>;

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
    </div>
  );
}
