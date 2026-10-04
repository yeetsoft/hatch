import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@hatch/ui';
import { fileTrunkBuildBug } from '../api/client';
import {
  buildIconTone,
  buildIconWords,
  questionEmptyWords,
  reviewEmptyWords,
  trunkBuildEmptyWords,
  trunkBuildHead,
  trunkIconTone,
  trunkIconWords,
  waitedWords,
} from '../lib/attention';
import { message } from '../lib/errors';
import { pullRequestWords } from '../lib/pullRequest';
import { useIssueConfirmations } from '../lib/useIssueConfirmations';
import type { Attention, TrunkBuild } from '../types';

/**
 * The build icon: a plain dot, coloured and named for one of the three build
 * states. No behaviour of its own - it sits inside the row's own link, so a
 * click anywhere, icon included, opens the pull request.
 */
function BuildIcon({ state }: { state: string }) {
  const words = buildIconWords(state);

  return (
    <svg
      className={`hatch-attention-icon hatch-attention-icon-${buildIconTone(state)}`}
      viewBox="0 0 12 12"
      role="img"
      aria-label={words}
      focusable="false"
    >
      <title>{words}</title>
      <circle cx="6" cy="6" r="5" fill="currentColor" />
    </svg>
  );
}

/** The up-to-date icon: the same plain dot, coloured and named for whether the branch holds the trunk. */
function TrunkIcon({ holdsTrunk, trunk }: { holdsTrunk: boolean | null; trunk: string | null }) {
  const words = trunkIconWords(holdsTrunk, trunk);

  return (
    <svg
      className={`hatch-attention-icon hatch-attention-icon-${trunkIconTone(holdsTrunk)}`}
      viewBox="0 0 12 12"
      role="img"
      aria-label={words}
      focusable="false"
    >
      <title>{words}</title>
      <circle cx="6" cy="6" r="5" fill="currentColor" />
    </svg>
  );
}

/**
 * One failing trunk: the trunk and repository as the runner reported them, the
 * short sha, every failing check as a link, and either the button that files
 * a bug or the bug already filed.
 *
 * A container and not one anchor, unlike every other row here - several of
 * its links and its button each need their own click, where a pull request or
 * a question row is one destination the whole row goes to.
 */
function TrunkBuildRow({ build, onFiled }: { build: TrunkBuild; onFiled: () => void }) {
  const [filing, setFiling] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { confirm } = useIssueConfirmations();

  async function fileBug() {
    setFiling(true);
    setError(null);
    try {
      const created = await fileTrunkBuildBug(build.id);
      confirm(created);
      onFiled();
    } catch (err) {
      setError(message(err));
    } finally {
      setFiling(false);
    }
  }

  return (
    <div className="hatch-attention-row hatch-attention-row-plain">
      <span className="hatch-attention-row-head">
        <span className="hatch-attention-key">{trunkBuildHead(build)}</span>
        <span className="hatch-attention-sha">{build.sha.slice(0, 10)}</span>
      </span>

      <span className="hatch-attention-checks">
        {build.failing.map((check, i) => (
          <span key={check.name}>
            {i > 0 && ', '}
            {/* Out of Hatch and into the forge, so a new tab - the panel
                somebody was reading should still be here when they come back. */}
            {check.url ? (
              <a href={check.url} target="_blank" rel="noreferrer noopener">
                {check.name}
              </a>
            ) : (
              check.name
            )}
          </span>
        ))}
      </span>

      <span className="hatch-attention-row-actions">
        {build.bugIssueKey ? (
          <Link to={`/issues/${build.bugIssueKey}`}>{build.bugIssueKey}</Link>
        ) : (
          <Button variant="primary" loading={filing} onClick={() => void fileBug()}>
            File a bug
          </Button>
        )}
      </span>

      {error && <p className="hatch-attention-row-error">{error}</p>}
    </div>
  );
}

/**
 * The Human half of what the loop is waiting on a person for: trunk builds
 * that fail, pull requests to review, questions to answer - in that order,
 * the order somebody would work through them.
 *
 * Exported alone, no outer heading and no `.hatch-attention-group` wrapper, so
 * its caller - the bar's `AttentionPanel`, beside the Agent group it draws
 * itself - can wrap it in its own (HA-279 removed the board's own "Waiting on
 * you" section, the other caller this once shared the contract with).
 * AttentionHuman.test.tsx still pins the no-wrapper shape, so it stays this
 * way rather than folding back into the panel.
 */
export function AttentionHuman({
  attention,
  now,
  reload,
}: {
  attention: Attention | null;
  now: Date;
  /** Re-reads the attention, so a row that just filed a bug shows its key for everybody drawing this. */
  reload: () => Promise<void>;
}) {
  const reviews = attention?.reviews ?? [];
  const questions = attention?.questions ?? [];
  const trunkBuilds = attention?.trunkBuilds ?? [];

  return (
    <>
      <section className="hatch-attention-section">
        <h3 className="hatch-attention-heading">Trunk builds that fail</h3>

        {trunkBuilds.length === 0 ? (
          <p className="hatch-attention-empty">{trunkBuildEmptyWords()}</p>
        ) : (
          <ul className="hatch-attention-rows">
            {trunkBuilds.map((t) => (
              <li key={t.id}>
                <TrunkBuildRow build={t} onFiled={() => void reload()} />
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="hatch-attention-section">
        <h3 className="hatch-attention-heading">Pull requests to review</h3>

        {reviews.length === 0 ? (
          <p className="hatch-attention-empty">{reviewEmptyWords(attention)}</p>
        ) : (
          <ul className="hatch-attention-rows">
            {reviews.map((r) => (
              <li key={r.key}>
                {/* Out of Hatch and into a different tool, so a new tab and a
                    plain anchor - the board somebody was reading should still
                    be here when they come back. The same call PullRequestLink
                    makes, for the same reason. */}
                <a
                  className="hatch-attention-row"
                  href={r.pullRequestUrl}
                  title={r.pullRequestUrl}
                  target="_blank"
                  rel="noreferrer noopener"
                >
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{r.key}</span>
                    <span className="hatch-attention-title">{r.title}</span>
                    <span className="hatch-attention-row-icons">
                      <BuildIcon state={r.buildState} />
                      <TrunkIcon holdsTrunk={r.holdsTrunk} trunk={r.trunk} />
                    </span>
                  </span>
                  {/* Drawn the way the issue page's chip draws it: the noise
                      off the front, nothing parsed out of the path. Hatch has
                      no opinion about whose forge an operator uses. */}
                  <span className="hatch-attention-url">{pullRequestWords(r.pullRequestUrl)}</span>
                </a>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="hatch-attention-section">
        <h3 className="hatch-attention-heading">Questions to answer</h3>

        {questions.length === 0 ? (
          <p className="hatch-attention-empty">{questionEmptyWords()}</p>
        ) : (
          <ul className="hatch-attention-rows">
            {questions.map((q) => (
              <li key={q.id}>
                {/* This tab, and to the issue rather than to anything here: a
                    question is answered on the issue page, with the options it
                    was asked with. Answering from here is out of scope on
                    purpose. */}
                <Link className="hatch-attention-row" to={`/issues/${q.issueKey}`}>
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{q.issueKey}</span>
                    <span className="hatch-attention-waited">{waitedWords(q.askedAt, now)}</span>
                  </span>
                  <span className="hatch-attention-question">{q.body}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </>
  );
}
