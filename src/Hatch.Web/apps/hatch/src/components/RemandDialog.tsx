import { Button, Modal } from '@hatch/ui';
import { remandColumn, remandTitle } from '../lib/remand';
import type { RemandOffer } from '../lib/remand';

/**
 * The question a card dropped on the dream cloud is asked: shelve this, and
 * what is under it, into a deferred column, and post a dream from it?
 *
 * The drop has moved nothing. Everything below is what the Remand press will
 * send, so the operator reads the column, the cards and the dream's first line
 * before anything is written. Leaving it on the board - the button, escape, the
 * scrim or the cross - closes the dialog and sends nothing.
 *
 * The column is a chooser only when the board has more than one deferred
 * column; with one it is a sentence. The dream's first line is the card's own
 * title, which is how the server starts the dream.
 *
 * Presentational, like CloseSubtreeDialog: the request, the busy flag and the
 * server's refusal are lib/useRemand.ts's.
 */
export function RemandDialog({
  offer,
  busy,
  error,
  onChoose,
  onConfirm,
  onClose,
}: {
  offer: RemandOffer | null;
  busy: boolean;
  error: string | null;
  onChoose: (statusId: number) => void;
  onConfirm: () => void;
  onClose: () => void;
}) {
  // Rendered unconditionally so the modal's own focus, escape and scrim
  // handling is the one that runs - see CloseSubtreeDialog.
  if (!offer) return <Modal open={false} onClose={onClose} title="" />;

  const count = offer.cards.length;
  const column = remandColumn(offer);

  return (
    <Modal open onClose={onClose} title={remandTitle(offer)}>
      <div className="hatch-cascade">
        {offer.columns.length > 1 ? (
          <label className="hatch-remand-column">
            <span>Shelve it in</span>
            <select value={offer.statusId} disabled={busy} onChange={(e) => onChoose(Number(e.target.value))}>
              {offer.columns.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
        ) : (
          <p>
            <code>{offer.key}</code> goes to {column.name}.
          </p>
        )}

        {count > 0 && (
          <>
            <p>
              {count === 1 ? 'One issue under it goes' : `${count} issues under it go`} too:
            </p>
            <ul className="hatch-cascade-list">
              {offer.cards.map((card) => (
                <li key={card.key}>
                  <code>{card.key}</code> — {card.title}
                </li>
              ))}
            </ul>
          </>
        )}

        <p>
          The dream opens with: <strong>{offer.title}</strong>
        </p>

        {error && <p className="text-danger">{error}</p>}

        <div className="hatch-form-actions">
          <Button onClick={onClose}>Leave it on the board</Button>
          <Button variant="primary" loading={busy} onClick={onConfirm}>
            Remand
          </Button>
        </div>
      </div>
    </Modal>
  );
}
