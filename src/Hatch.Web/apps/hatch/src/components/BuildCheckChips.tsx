import { buildTitle, checkLink, failedBuilds } from '../lib/buildCheck';
import type { BuildCheck } from '../types';

/**
 * The issue's build, said to fail: a chip beside the pull request's, one per
 * repository that does, with each failing check linked to its output.
 *
 * Draws nothing for a build that passed, is running, or has no checks, and
 * nothing for no verdict at all - the absence of this chip is the good news.
 * A check with no web address is named and not linked.
 */
export function BuildCheckChips({ checks }: { checks: readonly BuildCheck[] }) {
  const failed = failedBuilds(checks);
  const several = failed.length > 1;

  return (
    <>
      {failed.map((c) => (
        <span key={c.canonical} className="hatch-chip hatch-chip-conflict" title={buildTitle(c, several)}>
          Build fails:{' '}
          {c.failing.map((f, at) => {
            const href = checkLink(f);
            return (
              <span key={f.name}>
                {at > 0 ? ', ' : ''}
                {href === null ? (
                  f.name
                ) : (
                  <a href={href} target="_blank" rel="noreferrer noopener">
                    {f.name}
                  </a>
                )}
              </span>
            );
          })}
          {several ? ` in ${c.canonical}` : ''}
        </span>
      ))}
    </>
  );
}
