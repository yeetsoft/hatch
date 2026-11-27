import { useState } from 'react';
import { Button, Modal } from '@hatch/ui';
import { getIssue, moveIssue } from '../api/client';
import { pressFor, startSentences } from '../lib/ambitionPresses';
import { message } from '../lib/errors';
import { useMe } from '../lib/useMe';
import type { AmbitionRow } from '../types';

/**
 * Shape it and Start it - see HA-2845. Each is a plain move through the same
 * route a drag takes: no new permission, and the gates on the two columns
 * stay the board's. A refusal reads as the server's own sentence, beside the
 * press for Shape it and inside the dialog for Start it.
 *
 * `onMoved` is the caller's reload, so a row that just moved repaints from the
 * next read rather than this component guessing the new stage.
 */
export function AmbitionPresses({ row, onMoved }: { row: AmbitionRow; onMoved: () => void }) {
  const { me } = useMe();
  const press = pressFor(row, me);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [asking, setAsking] = useState<string[] | null>(null);

  if (!press) return null;

  const shape = async () => {
    setBusy(true);
    setError(null);
    try {
      await moveIssue(row.key, { statusId: row.shapingStatusId!, fromStatusId: row.statusId });
      onMoved();
    } catch (err) {
      setError(message(err));
    } finally {
      setBusy(false);
    }
  };

  const openStart = async () => {
    setError(null);
    try {
      const issue = await getIssue(row.key);
      setAsking(startSentences(row, issue.preApproval));
    } catch (err) {
      setError(message(err));
    }
  };

  const confirmStart = async () => {
    setBusy(true);
    setError(null);
    try {
      await moveIssue(row.key, { statusId: row.startStatusId!, fromStatusId: row.statusId });
      setAsking(null);
      onMoved();
    } catch (err) {
      setError(message(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="hatch-ambition-press">
      {press === 'shape' && (
        <Button onClick={() => void shape()} loading={busy}>
          Shape it
        </Button>
      )}
      {press === 'start' && (
        <Button onClick={() => void openStart()} loading={busy}>
          Start it
        </Button>
      )}

      {error && !asking && <p className="text-danger">{error}</p>}

      {asking && (
        <Modal open onClose={() => setAsking(null)} title={`Start ${row.key}?`}>
          {asking.map((sentence) => (
            <p key={sentence}>{sentence}</p>
          ))}

          {error && <p className="text-danger">{error}</p>}

          <div className="hatch-form-actions">
            <Button onClick={() => setAsking(null)}>Not yet</Button>
            <Button variant="primary" loading={busy} onClick={() => void confirmStart()}>
              Start it
            </Button>
          </div>
        </Modal>
      )}
    </div>
  );
}
