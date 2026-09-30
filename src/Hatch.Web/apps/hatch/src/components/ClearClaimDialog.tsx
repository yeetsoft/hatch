import { Button, Modal } from '@hatch/ui';
import { agoPhrase, claimedBySuffix } from '../lib/claim';
import type { IssueClaim } from '../types';

/**
 * The speed bump in front of taking a ticket back off a runner.
 *
 * It exists for one sentence, and the sentence is the consequence rather than
 * "are you sure": clearing the claim stops the runner, but not at once. Its
 * next heartbeat - within a minute by default - is refused, and that is what
 * ends the session; the ticket is claimable again from the moment the clear
 * lands, not from the moment the runner notices. That gap is worth knowing
 * before pressing, and nowhere else on the page says it.
 *
 * Presentational, like CloseSubtreeDialog, and rendered unconditionally for the
 * same reason: the modal's own focus, escape and scrim handling is the one that
 * should run. Dismissing by escape, the scrim or the ✕ means Leave it.
 */
export function ClearClaimDialog({
  issueKey,
  claim,
  busy,
  onConfirm,
  onClose,
}: {
  issueKey: string;
  /** The claim being cut off, or null when the dialog is shut. */
  claim: IssueClaim | null;
  /** The clear is in flight: the confirm stops taking presses and says so. */
  busy: boolean;
  onConfirm: () => void;
  onClose: () => void;
}) {
  if (!claim) return <Modal open={false} onClose={onClose} title="" />;

  return (
    <Modal open onClose={onClose} title={`Clear the claim on ${issueKey}?`}>
      <div className="hatch-claim-clear">
        <p>
          <strong>{claim.runner}</strong> is working {issueKey}
          {claimedBySuffix(claim)}, last heard from {agoPhrase(claim.heartbeatAt, new Date())}.
        </p>

        <p>
          Clearing the claim makes {issueKey} claimable again straight away, and stops the runner's session at its
          next heartbeat — <strong>within a minute by default</strong>. Until then that session may still push or
          move the ticket, so a second session claimed in the gap would be writing to it too.
        </p>

        <p className="text-muted">Clear it when the runner is known to be gone or known to be wrong.</p>

        <div className="hatch-form-actions">
          <Button onClick={onClose}>Leave it</Button>
          <Button variant="danger" loading={busy} onClick={onConfirm}>
            Clear the claim
          </Button>
        </div>
      </div>
    </Modal>
  );
}
