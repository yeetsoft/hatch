import { Button, Modal } from '@hatch/ui';

/**
 * The second look before a contribution moves into a column that merges pull
 * requests - see `confirmMove` (lib/protectedColumns.ts), which writes the
 * sentence. The server refuses nothing here: the move is allowed, and merging
 * somebody else's branch is exactly the thing that should not happen by
 * accident, from a drag that landed one column too far.
 *
 * Presentational, and rendered unconditionally for the reason
 * WipOverrideDialog is. Dismissing by escape, the scrim or the ✕ means Leave
 * it, and Leave it sends nothing.
 */
export function ContributionMoveDialog({
  asking,
  busy,
  error,
  onConfirm,
  onClose,
}: {
  asking: { key: string; sentence: string } | null;
  /** The move is in flight: the confirm stops taking presses and says so. */
  busy: boolean;
  /** What the move was refused with, as a sentence. Null while nothing has. */
  error: string | null;
  onConfirm: () => void;
  onClose: () => void;
}) {
  if (!asking) return <Modal open={false} onClose={onClose} title="" />;

  return (
    <Modal open onClose={onClose} title={`Move ${asking.key} in?`}>
      <div className="hatch-wip-override">
        <p>{asking.sentence}</p>

        {error && <p className="text-danger">{error}</p>}

        <div className="hatch-form-actions">
          <Button onClick={onClose}>Leave it</Button>
          <Button variant="danger" loading={busy} onClick={onConfirm}>
            Move it
          </Button>
        </div>
      </div>
    </Modal>
  );
}
