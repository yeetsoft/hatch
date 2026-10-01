import { Link } from 'react-router-dom';
import {
  conflictEmptyWords,
  failingBuildEmptyWords,
  resetWords,
} from '../lib/attention';
import { buildWords } from '../lib/buildCheck';
import { conflictWords } from '../lib/mergeCheck';
import { AttentionHuman } from './AttentionHuman';
import type { Attention } from '../types';

/**
 * What the control hands over when it is pressed: the links that unblock the
 * loop, in the order somebody would work through them - grouped into what a
 * person has to act on (Human) and what the loop is already on (Agent), so a
 * pull request held back by a conflict or a failed build never reads as
 * something waiting on a person.
 *
 * All five sections are always drawn, empty state included. A panel whose sections
 * appeared and disappeared would be a panel whose shape has to be re-read every
 * time it opens - and the empty states are not filler here: one of them is the
 * only place in Hatch that says an issue has sat in review with nowhere to
 * review it.
 */
export function AttentionPanel({
  attention,
  now,
  reload,
}: {
  attention: Attention | null;
  now: Date;
  /** Re-reads the attention, so a row that just filed a bug shows its key for everybody who has the panel open. */
  reload: () => Promise<void>;
}) {
  const conflicts = attention?.conflicts ?? [];
  const failingBuilds = attention?.failingBuilds ?? [];
  const exhaustedRunners = attention?.exhaustedRunners ?? [];

  return (
    <div className="hatch-attention-panel">
      {/* Unlike the four groups below, drawn only when it has rows: this is an
          alert about a condition that ends by itself, and nothing has to be
          pressed for it to disappear - see runners.ts and the ticket this
          shipped with. */}
      {exhaustedRunners.length > 0 && (
        <section className="hatch-attention-section hatch-attention-section-exhausted">
          <h3 className="hatch-attention-heading">Out of Claude usage</h3>

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

      <div className="hatch-attention-group">
        <h2 className="hatch-attention-group-heading">Human</h2>

        <AttentionHuman attention={attention} now={now} reload={reload} />
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
          <h3 className="hatch-attention-heading">Branch builds that fail</h3>

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
