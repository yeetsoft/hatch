import { useNavigate } from 'react-router-dom';
import { claimHealth, claimTitle, headClick, headKey } from '../lib/claim';
import { Robot } from './Robot';
import type { IssueClaim } from '../types';

/**
 * A robot head on a card that something is working right now - the same head
 * the nav's attention control draws, at card size, in blue (`fresh`) or amber
 * (`quiet`) rather than the control's own ink.
 *
 * `lit`, always: a claimed card is a runner awake on it, and the filled lamp
 * and eyes are what read as working. Unlike the nav's, this one never blinks -
 * App.css scopes the blink to the control's own class, so the colour is the
 * only thing this glyph adds, not the motion.
 *
 * Nothing at all where there is no claim: not a placeholder and not a dash, the
 * same register as the assignee chip beside it. Almost nothing on the board is
 * claimed at any moment, and a mark on every card is noise the eye has to skip
 * past to find the two that are live.
 *
 * There is no expiry here and there must not be one. A claim past its lease
 * arrives as null - the server did that arithmetic once, against one instant
 * per board scan - so this draws whatever it is handed. What it does judge is
 * narrower: a holder that has gone quiet on a lease it still holds, which is
 * worth seeing before the lease runs out rather than after.
 *
 * `linkRunner` makes the head itself a press to that runner's own page
 * (`/runners/<name>`) rather than inert ornament on the card. It is a `span`
 * carrying `role="link"` rather than a real `<a>` because the badge sits
 * inside the card's own `<Link to="/issues/...">`, which is also dnd-kit's
 * drag handle - a nested `<a>` is never valid markup, and `useNavigate` does
 * the rest. Without the prop, nothing here calls the hook at all, so a badge
 * drawn with no Router around it - QueueModal's - is unaffected.
 *
 * The press goes to `claim.claimedBy`, not `claim.runner`: a claim still
 * carries the checkout's own `--where` in the latter (a loop never sends its
 * stable name there), so `runner` is what to read in the sentence above and
 * `claimedBy` is the key's own name - the one `/runners/:name` actually
 * routes on.
 */
export function ClaimBadge({ claim, linkRunner = false }: { claim: IssueClaim | null; linkRunner?: boolean }) {
  if (!claim) return null;

  return linkRunner ? <LinkedHead claim={claim} /> : <Head claim={claim} />;
}

function Head({ claim }: { claim: IssueClaim }) {
  // Once per render, exactly as MomentChip does. Nothing here ticks: the card
  // goes stale with the board it was drawn from and comes back current on the
  // next read.
  const now = new Date();

  return (
    <span
      className={`hatch-card-claim hatch-card-claim-${claimHealth(claim, now)}`}
      title={claimTitle(claim, now)}
      aria-label={claimTitle(claim, now)}
    >
      <Robot lit className="hatch-card-claim-glyph" />
    </span>
  );
}

function LinkedHead({ claim }: { claim: IssueClaim }) {
  const navigate = useNavigate();
  const now = new Date();

  return (
    <span
      className={`hatch-card-claim hatch-card-claim-${claimHealth(claim, now)}`}
      title={claimTitle(claim, now)}
      aria-label={claimTitle(claim, now)}
      role="link"
      tabIndex={0}
      onClick={headClick(claim.claimedBy, navigate)}
      onKeyDown={headKey(claim.claimedBy, navigate)}
      onMouseDown={(e) => e.stopPropagation()}
      onTouchStart={(e) => e.stopPropagation()}
    >
      <Robot lit className="hatch-card-claim-glyph" />
    </span>
  );
}
