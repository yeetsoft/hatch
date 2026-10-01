import { claimHealth, claimTitle } from '../lib/claim';
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
 */
export function ClaimBadge({ claim }: { claim: IssueClaim | null }) {
  if (!claim) return null;

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
