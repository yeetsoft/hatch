import { Link } from 'react-router-dom';
import {
  agentSectionRowCounts,
  conflictEmptyWords,
  failingBuildEmptyWords,
  groupCollapsed,
  humanSectionRowCounts,
  resetWords,
} from '../lib/attention';
import { buildWords } from '../lib/buildCheck';
import { runnerPath } from '../lib/claim';
import { conflictWords } from '../lib/mergeCheck';
import { runnerFailureAdvice, runnerFailureRowWords } from '../lib/runnerFailureRows';
import { AttentionGroup, AttentionSection } from './AttentionFold';
import { AttentionHuman } from './AttentionHuman';
import type { Attention } from '../types';

/**
 * What the control hands over when it is pressed: the links that unblock the
 * loop, in the order somebody would work through them - grouped into what a
 * person has to act on (Human) and what the loop is already on (Agent), so a
 * pull request held back by a conflict or a failed build never reads as
 * something waiting on a person.
 *
 * A section or group with nothing in it is drawn as its heading alone, a
 * button that a press opens. *Pull requests to review* stays open - and so
 * keeps the Human group open - while its sentence carries a count
 * (`reviewWordsCarryAFact`), because that is the only place Hatch says an
 * issue has sat in review with nowhere to review it. The headings stay, so
 * the panel's shape is still readable at a glance.
 */
export function AttentionPanel({
  attention,
  now,
  reload,
  startOpen,
}: {
  attention: Attention | null;
  now: Date;
  /** Re-reads the attention, so a row that just filed a bug shows its key for everybody who has the panel open. */
  reload: () => Promise<void>;
  /** Starts every folded section and group open so a static-markup test can see the empty words. */
  startOpen?: boolean;
}) {
  const conflicts = attention?.conflicts ?? [];
  const failingBuilds = attention?.failingBuilds ?? [];
  const exhaustedRunners = attention?.exhaustedRunners ?? [];
  const runnerFailures = attention?.runnerFailures ?? [];

  return (
    <div className="hatch-attention-panel">
      {/* A failure a person fixes, so counted towards the badge - unlike the
          section below, which is a dot. Drawn only when it has rows, and gone
          at the next poll once the runner is well again or has gone quiet:
          nothing here is pressed away. */}
      {runnerFailures.length > 0 && (
        <section className="hatch-attention-section hatch-attention-section-failing">
          <h3 className="hatch-attention-heading">Failing runners</h3>

          <ul className="hatch-attention-rows">
            {runnerFailures.map((f) => (
              <li key={`${f.name}\n${f.where ?? ''}\n${f.kind}`}>
                <Link className="hatch-attention-row" to={runnerPath(f.name)}>
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{f.name}</span>
                    {f.where && <span className="hatch-attention-title">{f.where}</span>}
                  </span>
                  <span className="hatch-attention-files">{runnerFailureRowWords(f, now)}</span>
                  <span className="hatch-attention-files">{runnerFailureAdvice(f.kind)}</span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* Unlike the sections below, which fold when empty, drawn only when it has
          rows: this is an alert about a condition that ends by itself, and
          nothing has to be pressed for it to disappear - see runners.ts and
          the ticket this shipped with. */}
      {exhaustedRunners.length > 0 && (
        <section className="hatch-attention-section hatch-attention-section-exhausted">
          <h3 className="hatch-attention-heading">Out of usage</h3>

          <ul className="hatch-attention-rows">
            {exhaustedRunners.map((r) => (
              <li key={r.name}>
                <Link className="hatch-attention-row" to="/runners">
                  <span className="hatch-attention-row-head">
                    <span className="hatch-attention-key">{r.name}</span>
                    {r.where && <span className="hatch-attention-title">{r.where}</span>}
                  </span>
                  <span className="hatch-attention-files">{resetWords(r.exhaustedUntil, now)}</span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <AttentionGroup
        title="Human"
        collapsed={groupCollapsed(humanSectionRowCounts(attention))}
        startOpen={startOpen}
      >
        <AttentionHuman attention={attention} now={now} reload={reload} startOpen={startOpen} />
      </AttentionGroup>

      <AttentionGroup
        title="Agent"
        collapsed={groupCollapsed(agentSectionRowCounts(attention))}
        startOpen={startOpen}
      >
        <AttentionSection title="Branches that conflict" rows={conflicts.length} startOpen={startOpen}>
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
        </AttentionSection>

        <AttentionSection title="Branch builds that fail" rows={failingBuilds.length} startOpen={startOpen}>
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
        </AttentionSection>
      </AttentionGroup>
    </div>
  );
}
