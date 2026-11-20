import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '@hatch/ui';
import { getAmbitions, getStatuses } from '../api/client';
import { ScopeGate } from '../components/ScopeGate';
import { StatusPill } from '../components/StatusPill';
import { useLoaded } from '../lib/useLoaded';
import { useScope } from '../lib/useScope';
import type { AmbitionRow, Status } from '../types';

/** The two reads this page is made of, arriving together so a pill is never
    drawn without the colour of its column. */
interface AmbitionsView {
  ambitions: AmbitionRow[];
  statuses: Status[];
  /** The scope this was fetched under, in the form `scopeKey` below takes. */
  scopeKey: string;
}

/**
 * Every ambition across projects: what you are driving at, each checked against
 * the trunk on every pass. One row per ambition, with its capabilities as a
 * bar and the epics in flight beside it.
 *
 * It narrows to the tab's scope as `PlanPage` does, and draws only a fetch made
 * for the scope now in force.
 */
export function AmbitionsPage() {
  return (
    <ScopeGate>
      <AmbitionsContent />
    </ScopeGate>
  );
}

function AmbitionsContent() {
  const scope = useScope();

  /* The scope in force, as one string: `scope.ids` is empty until the
     provider's own project list lands, so a read made before that was made
     unscoped, and this is what tells the two apart. */
  const scopeKey = scope.isScoped ? scope.keys.join(',') : '';

  const load = useCallback(async (): Promise<AmbitionsView> => {
    const [ambitions, statuses] = await Promise.all([
      getAmbitions(scope.isScoped ? scope.ids : undefined),
      getStatuses(),
    ]);
    return { ambitions, statuses, scopeKey };
  }, [scope.isScoped, scope.ids, scopeKey]);

  const { data, error } = useLoaded<AmbitionsView>(load);

  /* Only a read made for the scope now in force is drawn: until the refetch the
     provider's late list triggers resolves, `data` is the unscoped one. */
  const current = data !== null && scope.loaded && data.scopeKey === scopeKey;

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data || !current) return <p className="text-muted">Loading…</p>;

  return (
    <div className="hatch-page">
      <PageHeader
        title="Ambitions"
        description="What you are driving at, across every project, each checked against the trunk on every pass."
      />

      {error && <p className="text-danger">{error}</p>}

      {/* The strip above the rows is drawn here, by the story after this one. */}

      <AmbitionList ambitions={data.ambitions} statuses={data.statuses} />
    </div>
  );
}

/** The rows, or the one sentence saying there are none in scope. */
export function AmbitionList({ ambitions, statuses }: { ambitions: AmbitionRow[]; statuses: Status[] }) {
  if (ambitions.length === 0) return <p className="text-muted">No ambition in scope.</p>;

  return (
    <ul className="hatch-ambition-list">
      {ambitions.map((row) => (
        <AmbitionRowView key={row.key} row={row} statuses={statuses} />
      ))}
    </ul>
  );
}

/** The column as a pill, or its name as muted text where the list lacks it. */
function ColumnPill({ statusId, statusName, statuses }: { statusId: number; statusName: string; statuses: Status[] }) {
  const status = statuses.find((s) => s.id === statusId);
  return status ? <StatusPill status={status} size="sm" /> : <span className="text-muted">{statusName}</span>;
}

/** The four counts of a bar, in the order they are drawn, each with the words
    the legend and the `aria-label` use. */
const SEGMENTS = [
  { count: (r: AmbitionRow) => r.holds, label: 'hold', cls: 'holds' },
  { count: (r: AmbitionRow) => r.inFlight, label: 'in flight', cls: 'in-flight' },
  { count: (r: AmbitionRow) => r.waitsOnPerson, label: 'waiting on a person', cls: 'waiting' },
  { count: (r: AmbitionRow) => r.notStarted, label: 'not started', cls: 'not-started' },
] as const;

/** One ambition: its head, its capabilities as a bar, and its open epics. */
export function AmbitionRowView({ row, statuses }: { row: AmbitionRow; statuses: Status[] }) {
  const drawn = SEGMENTS.map((s) => ({ ...s, n: s.count(row) })).filter((s) => s.n > 0);

  return (
    <li className="hatch-ambition-row">
      <div className="hatch-ambition-part">
        <Link to={`/issues/${row.key}`} className="hatch-ambition-title">
          {row.title}
        </Link>
        <span className="hatch-ambition-head-meta">
          <span className="hatch-plan-project">{row.projectKey}</span>
          <ColumnPill statusId={row.statusId} statusName={row.statusName} statuses={statuses} />
        </span>
      </div>

      <div className="hatch-ambition-part">
        <h3 className="hatch-ambition-heading">
          {row.capabilities} {row.capabilities === 1 ? 'capability' : 'capabilities'}
        </h3>
        {drawn.length === 0 ? (
          <p className="text-muted">This ambition has no capabilities yet.</p>
        ) : (
          <>
            {/* Widths are the counts in proportion: flex-grow carries the count
                and flex-basis is zero, so nothing is summed here. */}
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
        )}
      </div>

      <div className="hatch-ambition-part">
        <h3 className="hatch-ambition-heading">
          In flight, {row.open.length} of {row.limit}
        </h3>
        {row.open.length === 0 ? (
          <p className="text-muted">No epic is open under this ambition.</p>
        ) : (
          <>
            <p className="text-muted">
              {row.open.length} open, runs {row.limit} at once
            </p>
            <ul className="hatch-progress-list hatch-ambition-epics">
              {row.open.map((epic) => (
                <li key={epic.key} className="hatch-progress-row">
                  <Link to={`/issues/${epic.key}`} className="hatch-plan-key">
                    {epic.key}
                  </Link>
                  <span className="hatch-progress-title">{epic.title}</span>
                  <span className="hatch-epics-in-flight-meta">
                    <ColumnPill statusId={epic.statusId} statusName={epic.statusName} statuses={statuses} />
                    {epic.serves.length === 0 ? (
                      <span className="text-muted">&mdash;</span>
                    ) : (
                      epic.serves.map((position) => (
                        <span key={position} className="hatch-capability-badge">
                          #{position}
                        </span>
                      ))
                    )}
                  </span>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </li>
  );
}
