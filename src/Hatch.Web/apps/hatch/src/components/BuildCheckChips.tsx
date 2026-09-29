import { buildTitle, failedBuilds, PENDING } from '../lib/buildCheck';
import type { BuildCheck } from '../types';

/**
 * The issue's build, said to be failing: a chip beside the pull request's, one
 * per repository that is, with each failing check linked to its own page.
 *
 * Draws nothing for a build that passed, is still running with nothing failed
 * yet, or never ran, or for no verdict at all - the absence of this chip is the
 * good news. A build still running that already carries a failed check draws
 * the chip, marked as still running: that check's conclusion cannot un-fail it.
 */
export function BuildCheckChips({ checks }: { checks: readonly BuildCheck[] | undefined }) {
  const failed = failedBuilds(checks);
  const several = failed.length > 1;

  return (
    <>
      {failed.map((c) => (
        <span key={c.canonical} className="hatch-chip hatch-chip-build" title={buildTitle(c, several)}>
          Build failing{c.verdict === PENDING ? ', still running' : ''}:{' '}
          {c.failing.map((f, i) => (
            <span key={f.name}>
              {i > 0 && ', '}
              {/* Only where the board kept an address: it stores anything that
                  is not http(s) as null, and this is where that is relied on. */}
              {f.url ? (
                <a href={f.url} target="_blank" rel="noreferrer noopener">
                  {f.name}
                </a>
              ) : (
                f.name
              )}
            </span>
          ))}
          {several ? ` in ${c.canonical}` : ''}
        </span>
      ))}
    </>
  );
}
