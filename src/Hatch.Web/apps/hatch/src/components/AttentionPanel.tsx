import { Link } from 'react-router-dom';
import {
  buildIconTone,
  buildIconWords,
  conflictEmptyWords,
  failingBuildEmptyWords,
  questionEmptyWords,
  reviewEmptyWords,
  trunkIconTone,
  trunkIconWords,
  waitedWords,
} from '../lib/attention';
import { buildWords } from '../lib/buildCheck';
import { conflictWords } from '../lib/mergeCheck';
import { pullRequestWords } from '../lib/pullRequest';
import type { Attention } from '../types';

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
 * What the control hands over when it is pressed: the links that unblock the
 * loop, in the order somebody would work through them - grouped into what a
 * person has to act on (Human) and what the loop is already on (Agent), so a
 * pull request held back by a conflict or a failed build never reads as
 * something waiting on a person.
 *
 * All four sections are always drawn, empty state included. A panel whose sections
 * appeared and disappeared would be a panel whose shape has to be re-read every
 * time it opens - and the empty states are not filler here: one of them is the
 * only place in Hatch that says an issue has sat in review with nowhere to
 * review it.
 */
export function AttentionPanel({ attention, now }: { attention: Attention | null; now: Date }) {
  const reviews = attention?.reviews ?? [];
  const conflicts = attention?.conflicts ?? [];
  const failingBuilds = attention?.failingBuilds ?? [];
  const questions = attention?.questions ?? [];

  return (
    <div className="hatch-attention-panel">
      <div className="hatch-attention-group">
        <h2 className="hatch-attention-group-heading">Human</h2>

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
                      was asked with. Answering from the panel is out of scope on
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
      </div>

      <div className="hatch-attention-group">
        <h2 className="hatch-attention-group-heading">Agent</h2>

        <section className="hatch-attention-section">
          <h3 className="hatch-attention-heading">Branches that conflict</h3>

          {conflicts.length === 0 ? (
            <p className="hatch-attention-empty">{conflictEmptyWords()}</p>
          ) : (
            <ul className="hatch-attention-rows">
              {conflicts.map((c) => (
                <li key={c.key}>
                  {/* This tab, and to the issue: the loop is already at work on
                      it, so what a person wants from here is the page that says
                      which files and what has happened so far. */}
                  <Link className="hatch-attention-row" to={`/issues/${c.key}`}>
                    <span className="hatch-attention-row-head">
                      <span className="hatch-attention-key">{c.key}</span>
                      <span className="hatch-attention-title">{c.title}</span>
                    </span>
                    {c.checks.map((check) => (
                      <span key={check.canonical} className="hatch-attention-files" title={check.files.join('\n')}>
                        {conflictWords(check, c.checks.length > 1)}
                      </span>
                    ))}
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="hatch-attention-section">
          <h3 className="hatch-attention-heading">Builds that fail</h3>

          {failingBuilds.length === 0 ? (
            <p className="hatch-attention-empty">{failingBuildEmptyWords()}</p>
          ) : (
            <ul className="hatch-attention-rows">
              {failingBuilds.map((b) => (
                <li key={b.key}>
                  {/* This tab, and to the issue, as the conflicts are: the loop
                      is already at work on it, and the issue page links each
                      failing check. */}
                  <Link className="hatch-attention-row" to={`/issues/${b.key}`}>
                    <span className="hatch-attention-row-head">
                      <span className="hatch-attention-key">{b.key}</span>
                      <span className="hatch-attention-title">{b.title}</span>
                    </span>
                    {b.checks.map((check) => (
                      <span key={check.canonical} className="hatch-attention-files">
                        {buildWords(check, b.checks.length > 1)}
                      </span>
                    ))}
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    </div>
  );
}
