import { Outlet, useMatch, useOutletContext } from 'react-router-dom';
import { EmptyState, PageHeader } from '@hatch/ui';
import { RunnerList } from './RunnerList';
import { useRunners } from '../lib/useRunners';
import { useMe } from '../lib/useMe';
import { usePhone } from '../lib/viewport';
import type { Runner } from '../types';

/** What the layout hands its detail route through the `<Outlet/>` context. */
export interface RunnersOutlet {
  runners: Runner[] | null;
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

  const match = useMatch('/runners/:name');
  const selected = match?.params.name;

  const list = <RunnerList runners={runners} selected={selected} isAdmin={isAdmin} wallOff={wallOff} now={now} />;
  const header = <PageHeader title="Runners" />;
  const failed = error && <p className="text-danger">{error}</p>;

  if (phone) {
    if (match) {
      return (
        <div className="hatch-page">
          <Outlet context={{ runners }} />
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
          <Outlet context={{ runners }} />
        </section>
      </div>
    </div>
  );
}
