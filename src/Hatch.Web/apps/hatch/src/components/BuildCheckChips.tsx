import { buildTitle, failedBuilds } from '../lib/buildCheck';
import type { BuildCheck } from '../types';

/**
 * The issue's build, said to be failing: a chip beside the pull request's, one
 * per repository that is, with each failing check linked to its own page.
 *
 * Draws nothing for a build that passed, is still running or never ran, or for
 * no verdict at all - the absence of this chip is the good news.
 */
export function BuildCheckChips({ checks }: { checks: readonly BuildCheck[] | undefined }) {
  const failed = failedBuilds(checks);
  const several = failed.length > 1;

  return (
    <>
      {failed.map((c) => (
        <span key={c.canonical} className="hatch-chip hatch-chip-build" title={buildTitle(c, several)}>
          Build failing:{' '}
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
