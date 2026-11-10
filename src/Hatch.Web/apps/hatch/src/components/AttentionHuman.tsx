import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@hatch/ui';
import {
  approvedHeldEmptyWords,
  buildIconTone,
  buildIconWords,
  deadlockedEmptyWords,
  landingHeldEmptyWords,
  noCodeHeldEmptyWords,
  questionEmptyWords,
  reviewEmptyWords,
  runnerCapacityEmptyWords,
  runnerCapacityWords,
  trainStalledWords,
  trunkBuildEmptyWords,
  trunkBuildHead,
  trunkIconTone,
  trunkIconWords,
  waitedWords,
  waitingOnARunnerEmptyWords,
} from '../lib/attention';
import { lastSuccessWords } from '../lib/backup';
import { playbookFileWords } from '../lib/mergeCheck';
import { pullRequestWords } from '../lib/pullRequest';
import { TrunkBuildBugDialog } from './TrunkBuildBugDialog';
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
  const [open, setOpen] = useState(false);

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
          <Button variant="primary" onClick={() => setOpen(true)}>
            File a bug
          </Button>
        )}
      </span>

      <TrunkBuildBugDialog open={open} buildId={build.id} onClose={() => setOpen(false)} onFiled={onFiled} />
    </div>
  );
}

/**
 * The Human half of what the loop is waiting on a person for: issues waiting
 * on a runner, projects short on runner capacity, a backup that needs attention, stalled merge trains, deadlocked issues, trunk builds that fail, pull requests
 * to review, pull requests approved but not merging, issues landed but not
 * done, questions to answer - in that order, the order somebody would work
 * through them.
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
  const approvedHeld = attention?.approvedHeld ?? [];
  const landingHeld = attention?.landingHeld ?? [];
  const noCodeHeld = attention?.noCodeHeld ?? [];
  const deadlocked = attention?.deadlocked ?? [];
  const waitingOnARunner = attention?.waitingOnARunner ?? [];
  const runnerCapacity = attention?.runnerCapacity ?? [];
  const trainStalled = attention?.trainStalled ?? [];
  const backup = attention?.backup ?? null;

  return (
    <>
      {/* First, since it is the only one a person fixes by changing a runner,
          not the panel. Always drawn, with an empty state. */}
      <section className="hatch-attention-section">
        <h3 className="hatch-attention-heading">Waiting on a runner</h3>

        {waitingOnARunner.length === 0 ? (
          <p className="hatch-attention-empty">{waitingOnARunnerEmptyWords()}</p>
        ) : (
          <>
            <ul className="hatch-attention-rows">
              {waitingOnARunner.map((w) => (
                <li key={w.key}>
                  <Link className="hatch-attention-row" to={`/issues/${w.key}`}>
                    <span className="hatch-attention-row-head">
                      <span className="hatch-attention-key">{w.key}</span>
                      <span className="hatch-attention-title">{w.title}</span>
                    </span>
                    <span className="hatch-attention-files">{`in ${w.move} - ${w.missing}`}</span>
                  </Link>
                </li>
              ))}
            </ul>
            {/* Once, and outside every row: each row is already a link, and an
                anchor inside an anchor is invalid. */}
            <Link className="hatch-attention-row" to="/runners">
              <span className="hatch-attention-files">See the Runners page</span>
            </Link>
          </>
        )}
      </section>

      <section className="hatch-attention-section">
        <h3 className="hatch-attention-heading">Runner capacity</h3>

        {runnerCapacity.length === 0 ? (
          <p className="hatch-attention-empty">{runnerCapacityEmptyWords()}</p>
        ) : (
          <ul className="hatch-attention-rows">
            {runnerCapacity.map((row) => (
              <li key={row.projectKey}>
                <div className="hatch-attention-row hatch-attention-row-plain">
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{row.projectKey}</span>
                  </span>
                  <span className="hatch-attention-files">{runnerCapacityWords(row)}</span>
                  <span className="hatch-attention-checks">
                    {row.keys.map((key, i) => (
                      <span key={key}>
                        {i > 0 && ', '}
                        <Link to={`/issues/${key}`}>{key}</Link>
                      </span>
                    ))}
                  </span>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* Next, since a database with no copy outranks everything under it.
          Unlike Waiting on a runner it has no empty state: a healthy or
          unconfigured install must see nothing at all. */}
      {backup && (
        <section className="hatch-attention-section">
          <h3 className="hatch-attention-heading">Backups</h3>

          <ul className="hatch-attention-rows">
            <li>
              {/* This tab, and to Settings: the folder is fixed there. */}
              <Link className="hatch-attention-row" to="/settings">
                <span className="hatch-attention-row-head">
                  <span className="hatch-attention-key">Backups</span>
                </span>
                {/* nowrap and ellipsis, and a failure is a free-form error: the title keeps the whole of it reachable. */}
                <span className="hatch-attention-files" title={backup.sentence}>
                  {backup.sentence}
                </span>
                <span className="hatch-attention-files">{lastSuccessWords(backup)}</span>
              </Link>
            </li>
          </ul>
        </section>
      )}

      {trainStalled.length > 0 && (
        <section className="hatch-attention-section">
          <h3 className="hatch-attention-heading">Stalled merge trains</h3>

          <ul className="hatch-attention-rows">
            {trainStalled.map((t) => (
              <li key={t.projectKey}>
                <Link className="hatch-attention-row" to={`/issues/${t.headKey}`}>
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{t.headKey}</span>
                    <span className="hatch-attention-title">{t.projectName}</span>
                  </span>
                  {/* nowrap and ellipsis, and two runner names are long: the title keeps the whole sentence reachable. */}
                  <span className="hatch-attention-files" title={trainStalledWords(t)}>
                    {trainStalledWords(t)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="hatch-attention-section">
        <h3 className="hatch-attention-heading">Deadlocked</h3>

        {deadlocked.length === 0 ? (
          <p className="hatch-attention-empty">{deadlockedEmptyWords()}</p>
        ) : (
          <ul className="hatch-attention-rows">
            {deadlocked.map((d) => (
              <li key={d.key}>
                <Link className="hatch-attention-row" to={`/issues/${d.key}`}>
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{d.key}</span>
                    <span className="hatch-attention-title">{d.title}</span>
                  </span>
                  <span className="hatch-attention-files">{d.sentence}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>

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
                  {r.playbookFilePath && (
                    <span className="hatch-attention-files">{playbookFileWords(r.playbookFilePath)}</span>
                  )}
                </a>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="hatch-attention-section">
        <h3 className="hatch-attention-heading">Approved, not merging</h3>

        {approvedHeld.length === 0 ? (
          <p className="hatch-attention-empty">{approvedHeldEmptyWords()}</p>
        ) : (
          <ul className="hatch-attention-rows">
            {approvedHeld.map((h) => (
              <li key={h.key}>
                {/* This tab, and to the issue: the fix - moving it back to
                    review - happens there, not on the forge. */}
                <Link className="hatch-attention-row" to={`/issues/${h.key}`}>
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{h.key}</span>
                    <span className="hatch-attention-title">{h.title}</span>
                  </span>
                  <span className="hatch-attention-files">{h.sentence}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="hatch-attention-section">
        <h3 className="hatch-attention-heading">Landed, not done</h3>

        {landingHeld.length === 0 ? (
          <p className="hatch-attention-empty">{landingHeldEmptyWords()}</p>
        ) : (
          <ul className="hatch-attention-rows">
            {landingHeld.map((h) => (
              <li key={h.key}>
                <Link className="hatch-attention-row" to={`/issues/${h.key}`}>
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{h.key}</span>
                    <span className="hatch-attention-title">{h.title}</span>
                  </span>
                  <span className="hatch-attention-files">{h.sentence}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="hatch-attention-section">
        <h3 className="hatch-attention-heading">Finished with no code</h3>

        {noCodeHeld.length === 0 ? (
          <p className="hatch-attention-empty">{noCodeHeldEmptyWords()}</p>
        ) : (
          <ul className="hatch-attention-rows">
            {noCodeHeld.map((h) => (
              <li key={h.key}>
                <Link className="hatch-attention-row" to={`/issues/${h.key}`}>
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{h.key}</span>
                    <span className="hatch-attention-title">{h.title}</span>
                  </span>
                  <span className="hatch-attention-files">{h.reason}</span>
                </Link>
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
