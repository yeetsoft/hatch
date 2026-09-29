import { useState } from 'react';
import { Button, Modal, Text } from '@hatch/ui';
import { agePhrase, resetPhrase, toneClass } from '../lib/utilization';
import { useMe } from '../lib/useMe';
import type { Utilization } from '../types';

/**
 * Every window my own account is reporting.
 *
 * `@hatch/ui`'s Modal, which is what makes Escape and a click outside the panel
 * true without this file writing a key handler.
 *
 * It renders the reading the nav is already holding rather than fetching its
 * own, so opening and closing it repeatedly issues no request at all - and the
 * refresh below updates the battery and these rows together, because they are
 * one piece of state.
 *
 * The rows are the runner's list, in the runner's order. Nothing is sorted and
 * nothing is filtered - the account decided what to enforce and in what order,
 * and a tracker second-guessing that would be a tracker showing somebody a
 * different account from the one they have.
 */
export function UtilizationModal({
  open,
  onClose,
  reading,
  now,
  onRefresh,
}: {
  open: boolean;
  onClose: () => void;
  reading: Utilization;
  now: Date;
  onRefresh: () => Promise<void>;
}) {
  const [refreshing, setRefreshing] = useState(false);
  const { me } = useMe();

  async function refresh() {
    setRefreshing(true);
    try {
      await onRefresh();
    } finally {
      setRefreshing(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} title={me ? `${me.name}'s Claude usage` : 'My Claude usage'}>
      <div className="hatch-usage">
        <ul className="hatch-usage-rows">
          {reading.limits.map((limit, at) => (
            /* Keyed by position: the runner is the only thing that names
               these rows and two scoped rows can share a label, so the index
               is the one identifier that is actually unique here. */
            <li key={at} className={`hatch-usage-row ${toneClass(limit.tone)}`}>
              <span className="hatch-usage-label">{limit.label}</span>
              <span className="hatch-usage-percent">{Math.round(limit.percent)}%</span>
              <span className="hatch-usage-reset">{resetPhrase(limit.resetsAt, now)}</span>
            </li>
          ))}
        </ul>

        <div className="hatch-usage-foot">
          {/* The age of the number, always - a reported reading is as old as
              the last session, and it is worth knowing how old it is even
              when nothing has gone wrong. */}
          <Text tone="muted">
            {agePhrase(reading.readAt, now)}
            {reading.state === 'stale' && ' — no runner of mine has reported since'}
          </Text>
          <Button variant="secondary" disabled={refreshing} onClick={refresh}>
            {refreshing ? 'Refreshing…' : 'Refresh'}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
