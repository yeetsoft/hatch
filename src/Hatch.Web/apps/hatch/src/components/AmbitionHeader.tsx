import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Card, EmptyState, Field } from '@hatch/ui';
import { AmbitionPresses } from './AmbitionPresses';
import { preApprovalWord } from '../lib/preApproval';
import { scopedPath } from '../lib/scope';
import { wipLimitDraft, wipLimitHint, wipLimitLabel, wipLimitPlaceholder, wipLimitRequest } from '../lib/wip';
import type { AmbitionRow, AmbitionStage, Issue, Project } from '../types';

/** A stage as it is drawn: `budget spent` reads "Budget spent". Shared with
    `AmbitionsPage`'s row, so a stage reads the same word everywhere. */
export function stageWord(stage: AmbitionStage): string {
  return stage.charAt(0).toUpperCase() + stage.slice(1);
}

/** An ambition's stage as a pill - the Ambitions row's own drawing, so this
    page and that row can never disagree about what a stage looks like. */
export function StagePill({ stage }: { stage: AmbitionStage }) {
  return <span className={`hatch-stage-pill hatch-stage-${stage.replace(/ /g, '-')}`}>{stageWord(stage)}</span>;
}

/** The four counts of a bar, in the order they are drawn, each with the words
    the legend and the `aria-label` use. */
export const SEGMENTS = [
  { count: (r: AmbitionRow) => r.holds, label: 'hold', cls: 'holds' },
  { count: (r: AmbitionRow) => r.inFlight, label: 'in flight', cls: 'in-flight' },
  { count: (r: AmbitionRow) => r.waitsOnPerson, label: 'waiting on a person', cls: 'waiting' },
  { count: (r: AmbitionRow) => r.notStarted, label: 'not started', cls: 'not-started' },
] as const;

/** An ambition's capabilities as a bar and its legend - the Ambitions row's
    own drawing, so a row there and this page's header can never disagree
    about what a given row's bar looks like. */
export function CapabilityBar({ row }: { row: AmbitionRow }) {
  const drawn = SEGMENTS.map((s) => ({ ...s, n: s.count(row) })).filter((s) => s.n > 0);

  if (drawn.length === 0) return <EmptyState message="This ambition has no capabilities yet." />;

  return (
    <>
      {/* Widths are the counts in proportion: flex-grow carries the count and
          flex-basis is zero, so nothing is summed here. */}
      <div
        className="hatch-ambition-bar"
        role="img"
        aria-label={SEGMENTS.map((s) => `${s.count(row)} ${s.label}`).join(', ')}
      >
        {drawn.map((s) => (
          <span key={s.cls} className={`hatch-ambition-segment hatch-ambition-${s.cls}`} style={{ flexGrow: s.n }} />
        ))}
      </div>
      <ul className="hatch-ambition-legend">
        {drawn.map((s) => (
          <li key={s.cls}>
            <span className={`hatch-ambition-swatch hatch-ambition-${s.cls}`} aria-hidden="true" />
            {s.n} {s.label}
          </li>
        ))}
      </ul>
    </>
  );
}

/** The line an ambition's own page opens with: a way back to the Ambitions
    page, under the tab's own scope. */
export function AmbitionCrumb({ title, scopeKeys }: { title: string; scopeKeys: string[] }) {
  return (
    <p className="hatch-ambition-crumb">
      <Link to={scopedPath('/ambitions', scopeKeys)}>Ambitions</Link> / {title}
    </p>
  );
}

/** How many epics run at once, editable right where it is read - blur-commit,
    re-synced when the value changes underneath, the way `WipLimitField` on
    the issue page is. Saved through the same `onSetLimit`. */
function EpicsAtOnceField({ limit, onSetLimit }: { limit: number | null; onSetLimit: (limit: string) => void }) {
  const [draft, setDraft] = useState(wipLimitDraft(limit));
  const [known, setKnown] = useState(limit);

  if (limit !== known) {
    setKnown(limit);
    setDraft(wipLimitDraft(limit));
  }

  return (
    <Field label={wipLimitLabel('ambition')} hint={wipLimitHint('ambition')} as="div">
      <input
        inputMode="numeric"
        value={draft}
        placeholder={wipLimitPlaceholder('ambition')}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={() => {
          const next = wipLimitRequest(draft);
          if (next !== wipLimitDraft(limit)) onSetLimit(next);
        }}
      />
    </Field>
  );
}

/**
 * An ambition's own page opens with this, right after Details: the stage
 * HA-2843 computed for it, drawn the same word and sentence the Ambitions
 * row shows, its capability bar and legend, and a row of facts - the
 * project, how many epics run at once (the one control here), how many
 * passes have run, and the pre-approval level.
 *
 * `row` is read once, narrowed to this one key (`GET /api/ambitions?key=`),
 * so the stage this page draws can never disagree with what the Ambitions
 * page shows for the same ambition - see `AmbitionsController`. `row` is
 * null while that read is still in flight; `rowError` says why it never
 * arrived, and the facts below it still draw from `issue`.
 *
 * Shape it and Start it - see HA-2845 - sit under the stage sentence, drawn
 * by `AmbitionPresses` from the same `row`. `onMoved` is the reload this
 * page already does for everything else; a move repaints the whole header
 * from the next read rather than this component guessing the new stage.
 */
export function AmbitionHeader({
  issue,
  row,
  rowError,
  project,
  passCount,
  onSetLimit,
  onMoved,
}: {
  issue: Issue;
  row: AmbitionRow | null;
  rowError: string | null;
  project: Project | null;
  passCount: number | null;
  onSetLimit: (limit: string) => void;
  onMoved: () => void;
}) {
  return (
    <Card className="hatch-ambition-header">
      {rowError ? (
        <p className="text-danger">{rowError}</p>
      ) : (
        row && (
          <>
            <div className="hatch-ambition-header-stage">
              <StagePill stage={row.stage} />
              <p className="hatch-ambition-why">{row.stageWhy}</p>
              <AmbitionPresses row={row} onMoved={onMoved} />
            </div>
            <CapabilityBar row={row} />
          </>
        )
      )}

      <div className="hatch-ambition-header-facts">
        <Field label="Project" as="div">
          {project?.key ?? issue.projectKey}
        </Field>
        <EpicsAtOnceField limit={issue.wipLimit} onSetLimit={onSetLimit} />
        {passCount !== null && (
          <Field label="Passes" as="div">
            {passCount} {passCount === 1 ? 'pass' : 'passes'} so far
          </Field>
        )}
        <Field label="Pre-approval" as="div">
          {preApprovalWord(issue.preApproval)}
        </Field>
      </div>
    </Card>
  );
}
