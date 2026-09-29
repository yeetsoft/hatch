import { Button, Modal } from '@hatch/ui';
import { refusalText } from '../lib/wipOverride';
import type { WipRefusal } from '../types';

/**
 * The speed bump between the server's refusal and the same request sent
 * again with `wipOverride: true`.
 *
 * The browser never decides that the section is full - HA-89's `409` does.
 * This dialog only reads the sentence the server sent back and offers to
 * resend the request with the flag set; it computes nothing about the count
 * itself, because a stale board's own idea of the count is exactly what put
 * the card here in the first place.
 *
 * Presentational, like ClearClaimDialog and CloseSubtreeDialog, and rendered
 * unconditionally for the same reason: the modal's own focus, escape and
 * scrim handling is the one that should run. Dismissing by escape, the scrim
 * or the ✕ means Leave it.
 */
export function WipOverrideDialog({
  asking,
  busy,
  error,
  onConfirm,
  onClose,
}: {
  asking: { key: string; refusal: WipRefusal } | null;
  /** The retry is in flight: the confirm stops taking presses and says so. */
  busy: boolean;
  /** What the retry was refused with, as a sentence. Null while nothing has. */
  error: string | null;
  onConfirm: () => void;
  onClose: () => void;
}) {
  if (!asking) return <Modal open={false} onClose={onClose} title="" />;

  return (
    <Modal open onClose={onClose} title={`Move ${asking.key} in anyway?`}>
      <div className="hatch-wip-override">
        <p>{refusalText(asking.refusal)}</p>

        <p className="text-muted">
          The history will say you moved it in past the limit, and nothing more is pulled in until the section has
          room.
        </p>

        {error && <p className="text-danger">{error}</p>}

        <div className="hatch-form-actions">
          <Button onClick={onClose}>Leave it</Button>
          <Button variant="danger" loading={busy} onClick={onConfirm}>
            Move anyway
          </Button>
        </div>
      </div>
    </Modal>
  );
}
