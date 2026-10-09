import { useState } from 'react';
import { Button } from '@hatch/ui';
import { buildTimePhrase, earnsRevisionNotice, isStamped, shortSha } from '../lib/revision';
import type { Revision } from '../types';

/**
 * The muted block at the foot of the gear's panel: what commit this replica
 * is running, and whether this page's own bundle has fallen behind it.
 *
 * Pure, and the only piece of HA-212 worth testing with no DOM - the split is
 * `NavUtilization` / `UtilizationBattery`'s. `revision === null` covers both
 * "the panel hasn't been opened yet" and "the read failed", which is exactly
 * what `useRevision` hands back in either case, so there is nothing further
 * to tell them apart here.
 */
export function BuildStamp({ revision }: { revision: Revision | null }) {
  if (revision === null) return null;

  // Once per render, exactly as ClaimBadge does. Nothing here ticks: the
  // phrase goes stale with the panel it was drawn in and comes back current
  // the next time it's opened, which re-fetches nothing (see useRevision).
  const now = new Date();

  return (
    <>
      <hr className="hatch-menu__divider" />
      <div className="hatch-build-stamp">
        {isStamped(revision.revision) ? (
          <StampedLine revision={revision} now={now} />
        ) : (
          <p className="hatch-build-stamp__line">dev build</p>
        )}
      </div>
    </>
  );
}

function StampedLine({ revision, now }: { revision: Revision; now: Date }) {
  // "Copied" is about the sha that was copied, so it must reset if the sha
  // ever changes underneath - the same reason `Command` resets `said` on a
  // changed `command`. In practice the sha never changes under a mounted
  // BuildStamp (HA-207 criterion 2: no polling), but copying the rule is
  // cheaper than special-casing its absence.
  const [said, setSaid] = useState<string | null>(null);
  const [known, setKnown] = useState(revision.revision);

  if (revision.revision !== known) {
    setKnown(revision.revision);
    setSaid(null);
  }

  // Read at render rather than held: whether this document may reach the
  // clipboard is a fact about the page, and it does not change under us.
  const canCopy = typeof navigator !== 'undefined' && typeof navigator.clipboard?.writeText === 'function';

  async function copy() {
    try {
      await navigator.clipboard.writeText(revision.revision);
      setSaid('Copied');
    } catch {
      setSaid('Select it and copy');
    }
  }

  const phrase = buildTimePhrase(revision.revision, revision.builtAt, now);

  return (
    <>
      <p className="hatch-build-stamp__line">
        {canCopy ? (
          <button
            type="button"
            className="hatch-build-stamp__sha"
            title={revision.revision}
            onClick={() => void copy()}
          >
            {shortSha(revision.revision)}
          </button>
        ) : (
          <code className="hatch-build-stamp__sha" title={revision.revision}>
            {shortSha(revision.revision)}
          </code>
        )}
        {said !== null && <span className="hatch-build-stamp__said">{said}</span>}
        {phrase !== null && ` · ${phrase.absolute} · ${phrase.relative}`}
      </p>
      {earnsRevisionNotice(revision.client) && (
        <p className="hatch-build-stamp__notice">
          a newer build is live
          <Button onClick={() => location.reload()}>Reload</Button>
        </p>
      )}
    </>
  );
}
