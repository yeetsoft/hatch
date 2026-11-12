import { useEffect, useId, useRef } from 'react';
import type { ReactNode } from 'react';
import { panelTakesFocus } from './modalFocus';
import './Modal.css';

export type ModalWidth = 'default' | 'narrow';

/** The ground the panel is painted on. `card` is the house dialog; `console` is the dark instrument ground the
    palette configurator is drawn for (`--console`, with its `--console-*` siblings). */
export type ModalTone = 'card' | 'console';

export interface ModalProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  children?: ReactNode;
  /** The row that has to stay reachable, drawn under the body rather than in
      it. Given one, the panel stops scrolling and the body scrolls inside it,
      so a dialog ends in its actions however much is above them. Left off, the
      panel is the scroller it has always been. */
  footer?: ReactNode;
  /** The house width every caller gets unless it asks otherwise. `narrow` is
      for a dialog whose content is a reading measure of its own, not the
      house's 720px. */
  width?: ModalWidth;
  /** `console` paints the panel on `--console` with `--on-console` ink, for a dialog whose content is drawn for
      that ground. Left off, the card it has always been. */
  tone?: ModalTone;
}

/**
 * The dialog. Admin had this as its own component and it moves here whole —
 * escape to close, click the scrim to close, and a panel that wears the card's
 * own class rather than a second copy of a card's paint. It composes that
 * class rather than rendering <Card> because the panel needs a ref and four
 * ARIA attributes on the same element, and a <Card> that forwarded all of
 * those would be a <div> with extra steps.
 *
 * Three things it gained on the way, none of which move a pixel:
 *
 * - **It says it is a dialog.** `role="dialog"`, `aria-modal`, and a
 *   `aria-labelledby` pointing at its own heading. Before this it was a div
 *   with a click handler, and a screen reader had no way to know the page
 *   behind it was inert.
 * - **Focus goes into it when it opens and comes back when it closes.** Admin's
 *   version left focus on whatever button opened it, so a keyboard user pressed
 *   Tab and walked the page *behind* the scrim.
 * - **A child that took focus on open keeps it.** React applies a child's
 *   `autoFocus` at commit, before this component's effect, so the panel steps
 *   in only when focus is not already inside it.
 * - **Escape is bound to the document, not to the window**, so it still fires
 *   from inside an input in the panel.
 * - **It can pin its actions.** `footer` puts a row under the body instead of
 *   in it, and the body becomes the scroller. Before this the only way to keep
 *   a long dialog's buttons on screen was to cap the content by hand against
 *   the panel's chrome, which is a number that is wrong on the next display.
 *
 * It is deliberately not a full focus trap. That wants either `<dialog>`'s
 * top-layer behaviour or a well-tested library, and picking between those is a
 * bigger decision than this phase gets to make. Focus starts inside and
 * returns; the rest is listed in the plan.
 *
 * The title stays an <h3>. Under the page's <h1> that skips a level, which is
 * a known outline gap this phase inherited rather than caused: lifting it to
 * <h2> would move it from 18px to 22px, and Phase 4 may not change type a
 * designer has not chosen.
 *
 * `onClose` is read from a ref rather than named in the open effect's
 * dependencies, so a caller that passes a new arrow on every render (an
 * inline `() => setOpen(false)`) does not re-run the effect and re-steal
 * focus on every parent re-render — a 30s board poll, or an optimistic
 * repaint. Only `open` flipping should touch focus.
 *
 * Escape acts only when this panel is the *last* `[aria-modal="true"]` in the
 * document. Dialogs here are not portalled, so a dialog stacked over another
 * (the close-subtree offer drawn over the issue peek) sits later in the DOM
 * at the same z-index and paints on top — and both panels' `keydown`
 * listeners fire on the same event before either close commits, so without
 * this check one Escape would close both.
 *
 * The opener is recorded during render, not in the effect: by the time the
 * effect runs an `autoFocus` child has already taken focus, and the effect
 * would record that child rather than the button that opened the dialog. The
 * ref write is safe under StrictMode's double render, since the second pass
 * finds `wasOpenRef` already set. A server render has no `document` and no effect,
 * so it records nothing.
 */
export function Modal({ open, onClose, title, children, footer, width = 'default', tone = 'card' }: ModalProps) {
  const titleId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const openerRef = useRef<Element | null>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  const wasOpenRef = useRef(false);
  if (open && !wasOpenRef.current && typeof document !== 'undefined') openerRef.current = document.activeElement;
  wasOpenRef.current = open;

  useEffect(() => {
    if (!open) return;

    // The panel itself, rather than the first control in it: a dialog that
    // lands on "Delete" is a dialog that deletes on Enter. A child that took
    // focus on open (an `autoFocus` field) keeps it.
    const panel = panelRef.current;
    if (panel && panelTakesFocus(panel, document.activeElement)) panel.focus();

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      const panels = document.querySelectorAll('[aria-modal="true"]');
      if (panels[panels.length - 1] !== panelRef.current) return;
      onCloseRef.current();
    };
    document.addEventListener('keydown', onKeyDown);

    return () => {
      document.removeEventListener('keydown', onKeyDown);
      const opener = openerRef.current;
      if (opener instanceof HTMLElement && document.contains(opener)) opener.focus();
    };
  }, [open]);

  if (!open) return null;

  const framed = Boolean(footer);

  return (
    <div
      className="hatch-modal__overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className={`hatch-card hatch-modal__panel${framed ? ' hatch-modal__panel--framed' : ''}${width === 'narrow' ? ' hatch-modal__panel--narrow' : ''}${tone === 'console' ? ' hatch-modal__panel--console' : ''}`}
      >
        <div className="hatch-modal__head">
          <h3 id={titleId}>{title}</h3>
          <button type="button" className="hatch-modal__close" onClick={onClose} aria-label="Close">
            ✕
          </button>
        </div>
        {framed ? (
          <>
            <div className="hatch-modal__body">{children}</div>
            <div className="hatch-modal__foot">{footer}</div>
          </>
        ) : (
          children
        )}
      </div>
    </div>
  );
}
