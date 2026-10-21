import { useEffect, useRef, useState } from 'react';
import { attentionLabel, attentionTone, closesOnPress, pillCount } from '../lib/attention';
import { useAttentionContext } from '../lib/useAttentionContext';
import { useAttentionNow } from '../lib/useAttentionNow';
import { AttentionPanel } from './AttentionPanel';
import { Robot } from './Robot';

/**
 * Whether the loop is waiting on a person, at the right end of the bar.
 *
 * Quiet while it is not: muted ink, no fill, no border, a glyph and one word.
 * Loud the moment it is: the glyph lights, and one count pill per non-empty
 * section appears. On the desk it measures the same in both states, because a
 * widget that grew when something arrived would shove the battery leftwards
 * and move the bar out from under the cursor. A phone has the width for
 * neither: four pills with two-digit counts are wider than the row can hold
 * beside the battery and the overnight control, so there (App.css) they fold
 * into the one total drawn after them, which the desk hides.
 *
 * Never returns null. Unlike the battery - which draws nothing on an install
 * with no Claude token, because there is no such thing as its answer there -
 * "nothing is waiting" is an answer worth drawing, and it is most of the time.
 *
 * A dropdown rather than `@hatch/ui`'s Modal: the panel is a short list of
 * links anchored to the thing that opened it, and a modal's overlay would dim
 * the board behind a list of links back into it.
 */
export function NavAttention() {
  const { attention, reload } = useAttentionContext();
  const [open, setOpen] = useState(false);
  const now = useAttentionNow(open);
  const container = useRef<HTMLDivElement>(null);

  const tone = attentionTone(attention);
  const label = attentionLabel(attention);
  const reviews = attention?.reviews.length ?? 0;
  const questions = attention?.questions.length ?? 0;
  const exhausted = attention?.exhaustedRunners?.length ?? 0;
  const trunkBuilds = attention?.trunkBuilds?.length ?? 0;
  const deadlocked = attention?.deadlocked?.length ?? 0;
  const pills = pillCount(attention);

  /* A pointer outside it closes it. Clicking a non-focusable area of the page
     fires no blur at all, which is why this exists as well as the focusout
     below - the same pair IssuePicker uses. */
  useEffect(() => {
    if (!open) return;
    const outside = (e: PointerEvent) => {
      if (!container.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', outside);
    return () => document.removeEventListener('pointerdown', outside);
  }, [open]);

  return (
    <div
      className="hatch-attention"
      ref={container}
      /* React's onBlur is focusout, so it bubbles: Tab out of the last link in
         the panel moves focus onward natively and the panel goes with it, with
         no Tab handling and no preventDefault anywhere near it. */
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget)) setOpen(false);
      }}
      /* A row followed closes it, as `Menu.Item` does: nothing else would, since
         the link is inside the container (no outside press) and keeps focus (no
         blur), and on a phone the panel would sit over the page it just loaded.
         One handler here rather than a callback through every row. The click
         is not stopped, so the link navigates as it was going to. */
      onClick={(e) => {
        const link = (e.target as Element).closest('.hatch-attention-panel a');
        if (closesOnPress(link as HTMLAnchorElement | null, e.nativeEvent)) setOpen(false);
      }}
      onKeyDown={(e) => {
        if (e.key !== 'Escape' || !open) return;
        e.preventDefault();
        setOpen(false);
        // Back to what opened it, rather than nowhere: Escape from inside a
        // list of links should not cost somebody their place in the page.
        container.current?.querySelector('button')?.focus();
      }}
    >
      <button
        type="button"
        className={`hatch-attention-control hatch-attention-${tone}`}
        onClick={() => setOpen((was) => !was)}
        title={label}
        aria-label={label}
        aria-haspopup="true"
        aria-expanded={open}
      >
        <span className="hatch-attention-glyph-wrap">
          <Robot lit={tone === 'asking'} className="hatch-attention-glyph" />
          {/* A dot, not a pill: a runner out of usage is not a count of
              anything to fix, so it never changes the control's width or its
              review-and-question pills - only the accessible name above does
              that, with a phrase of its own. */}
          {exhausted > 0 && <span className="hatch-attention-dot" aria-hidden="true" />}
        </span>

        {tone === 'rest' ? (
          <span className="hatch-attention-word">Clear</span>
        ) : (
          <span className="hatch-attention-pills">
            {/* A digit in each pill, not a dot: the loud state has to be
                legible without colour, and the accessible name above says the
                same thing in words. A pill per non-empty section, so an empty
                half takes no room rather than showing a zero. */}
            {deadlocked > 0 && (
              <span className="hatch-attention-pill hatch-attention-pill-deadlocked">{deadlocked}</span>
            )}
            {trunkBuilds > 0 && <span className="hatch-attention-pill hatch-attention-pill-trunk">{trunkBuilds}</span>}
            {reviews > 0 && <span className="hatch-attention-pill hatch-attention-pill-review">{reviews}</span>}
            {questions > 0 && <span className="hatch-attention-pill hatch-attention-pill-question">{questions}</span>}
          </span>
        )}
        {tone === 'asking' && pills > 0 && (
          <span className="hatch-attention-pill hatch-attention-pill-total">{pills}</span>
        )}
      </button>

      {open && <AttentionPanel attention={attention} now={now} reload={reload} />}
    </div>
  );
}
