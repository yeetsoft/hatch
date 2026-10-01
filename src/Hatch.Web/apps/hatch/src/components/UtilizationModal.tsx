import { useState } from 'react';
import { Button, Modal, Text } from '@hatch/ui';
import { agePhrase, rowPercent, rowResetPhrase, rowSentence, timeGoneFraction, usageVars } from '../lib/utilization';
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
    <Modal
      open={open}
      onClose={onClose}
      title={me ? `${me.name}'s Claude usage` : 'My Claude usage'}
      width="narrow"
    >
      <div className="hatch-usage">
        <ul className="hatch-usage-rows">
          {reading.limits.map((limit, at) => {
            const percent = rowPercent(limit, now);
            const goneFraction = timeGoneFraction(limit.window, limit.resetsAt, now);
            // A monthly credit limit is not a window whose length Hatch knows,
            // so `extra` gets no time bar at all - not an empty or unknown one.
            const hasTimeBar = limit.window !== 'extra';

            return (
              /* Keyed by position: the runner is the only thing that names
                 these rows and two scoped rows can share a label, so the index
                 is the one identifier that is actually unique here. */
              <li key={at} className="hatch-usage-row">
                <span className="hatch-usage-label">{limit.label}</span>
                <span className="hatch-usage-percent">{Math.round(percent)}%</span>
                <span className="hatch-usage-reset">{rowResetPhrase(limit, now)}</span>

                {/* One picture, one sentence - the way StatusMeter.tsx announces
                    its bar - rather than two unlabelled tracks a screen reader
                    would have to guess the relationship between. */}
                <div className="hatch-usage-bars" role="img" aria-label={rowSentence(limit, now)}>
                  <div className="hatch-usage-bar-track">
                    <div className="hatch-usage-bar-fill" style={{ ...usageVars(percent), width: `${percent}%` }} />
                  </div>
                  {hasTimeBar && (
                    <div
                      className={`hatch-usage-bar-track${goneFraction === null ? ' hatch-usage-bar-track-unknown' : ''}`}
                    >
                      {goneFraction !== null && (
                        <div className="hatch-usage-time-fill" style={{ width: `${goneFraction * 100}%` }} />
                      )}
                    </div>
                  )}
                </div>
              </li>
            );
          })}
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
