import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import { Card, EmptyState, PageHeader } from '@hatch/ui';
import { getAmbitions, getBallot, getStatuses } from '../api/client';
import { ScopeGate } from '../components/ScopeGate';
import { StatusPill } from '../components/StatusPill';
import { waitedWords } from '../lib/attention';
import { firstLine } from '../lib/dreams';
import { message } from '../lib/errors';
import { scopedPath } from '../lib/scope';
import { useLoaded } from '../lib/useLoaded';
import { useScope } from '../lib/useScope';
import type { AmbitionRow, AmbitionStage, Ballot, BallotNode, Question, Status } from '../types';

/** The reads this page is made of, arriving together so a pill is never drawn
    without the colour of its column. The ballot is the exception in what it
    does on failure: it is one strip on the page, so its failure is carried
    here as a sentence and never rejects the rest. */
export interface AmbitionsView {
  ambitions: AmbitionRow[];
  statuses: Status[];
  ballot: Ballot | null;
  ballotError: string | null;
  /** The scope this was fetched under, in the form `scopeKey` below takes. */
  scopeKey: string;
}

/**
 * Every ambition across projects: what you are driving at, each checked against
 * the trunk on every pass. One row per ambition, grouped by stage, with what it
 * needs next, its capabilities as a bar and the epics in flight beside it.
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
    /* Handlers are attached at the call, so a ballot that fails is caught here
       and handed to the strip rather than blanking the rows. */
    const [ambitions, statuses, read] = await Promise.all([
      getAmbitions(scope.isScoped ? scope.ids : undefined),
      getStatuses(),
      getBallot(scope.isScoped ? scope.ids : undefined).then(
        (b) => ({ ballot: b, failure: null as string | null }),
        (err: unknown) => ({ ballot: null, failure: message(err) }),
      ),
    ]);
    return { ambitions, statuses, ballot: read.ballot, ballotError: read.failure, scopeKey };
  }, [scope.isScoped, scope.ids, scopeKey]);

  const { data, error } = useLoaded<AmbitionsView>(load);

  /* Only a read made for the scope now in force is drawn: until the refetch the
     provider's late list triggers resolves, `data` is the unscoped one. */
  const current = data !== null && scope.loaded && data.scopeKey === scopeKey;

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data || !current) return <p className="text-muted">Loading…</p>;

  return <AmbitionsBody data={data} scopeKeys={scope.isScoped ? scope.keys : []} now={new Date()} error={error} />;
}

/** The page under its gate: the header, the strip of what is waiting, then the
    rows. Apart from the loading so a test can draw it from props. */
export function AmbitionsBody({
  data,
  scopeKeys,
  now,
  error = null,
}: {
  data: AmbitionsView;
  scopeKeys: string[];
  now: Date;
  error?: string | null;
}) {
  return (
    <div className="hatch-page">
      <PageHeader
        title="Ambitions"
        description="What you are driving at, across every project, each checked against the trunk on every pass."
      />

      {error && <p className="text-danger">{error}</p>}

      <WaitingStrip ballot={data.ballot} error={data.ballotError} scopeKeys={scopeKeys} now={now} />

      <AmbitionList ambitions={data.ambitions} statuses={data.statuses} />
    </div>
  );
}

/** Every question at `hold` on an ambition's tree, with the ambition's title.
    The ballot already orders roots, children and each node's questions, so
    this walks it as it comes: a node's own questions, then each child. */
export function holdQuestions(ballot: Ballot): { ambition: string; question: Question }[] {
  const found: { ambition: string; question: Question }[] = [];

  const walk = (node: BallotNode, ambition: string) => {
    for (const question of node.questions) if (question.level === 'hold') found.push({ ambition, question });
    for (const child of node.children) walk(child, ambition);
  };

  for (const root of ballot.roots) if (root.type === 'ambition') walk(root, root.title);
  return found;
}

/** The hold-level questions on the ambitions in scope, each a line linking to
    the ballot; or one sentence where there are none or the read failed. */
export function WaitingStrip({
  ballot,
  error,
  scopeKeys,
  now,
}: {
  ballot: Ballot | null;
  error: string | null;
  scopeKeys: string[];
  now: Date;
}) {
  if (error || !ballot) {
    return <p className="text-danger hatch-ambition-failure">What is waiting on you could not be read: {error ?? 'no answer'}</p>;
  }

  const lines = holdQuestions(ballot);
  if (lines.length === 0) return <EmptyState message="Nothing is waiting on you." />;

  return (
    <Card as="section" className="hatch-ambition-strip">
      <h2 className="hatch-section-title">
        {lines.length} {lines.length === 1 ? 'decision' : 'decisions'} waiting on you
      </h2>
      <ul className="hatch-ambition-strip-lines">
        {lines.map(({ ambition, question }) => (
          <li key={question.id}>
            <Link to={scopedPath('/ballot', scopeKeys)} className="hatch-ambition-strip-line">
              <strong>{ambition}</strong>
              <span className="hatch-plan-key">{question.issueKey}</span>
              <span>{firstLine(question.body)}</span>
              <span className="text-muted">{waitedWords(question.askedAt, now)}</span>
            </Link>
          </li>
        ))}
      </ul>
    </Card>
  );
}

/** The stages whose ambitions are folded away at the foot of the list. */
const FINISHED: readonly AmbitionStage[] = ['done', 'shelved'];

/** A stage as it is drawn: `budget spent` reads "Budget spent". */
function stageWord(stage: AmbitionStage): string {
  return stage.charAt(0).toUpperCase() + stage.slice(1);
}

/**
 * The rows under a heading for each stage, then the finished ones folded under
 * one line; or the one sentence saying there are none in scope.
 *
 * The server sends the rows grouped and in order, so this only cuts them where
 * the stage changes and never sorts: a group's place is where its first row is.
 */
export function AmbitionList({ ambitions, statuses }: { ambitions: AmbitionRow[]; statuses: Status[] }) {
  if (ambitions.length === 0) return <EmptyState message="No ambition in scope." />;

  const groups = new Map<AmbitionStage, AmbitionRow[]>();
  const finished: AmbitionRow[] = [];
  for (const row of ambitions) {
    if (FINISHED.includes(row.stage)) finished.push(row);
    else groups.set(row.stage, [...(groups.get(row.stage) ?? []), row]);
  }

  const list = (rows: AmbitionRow[]) => (
    <ul className="hatch-ambition-list">
      {rows.map((row) => (
        <AmbitionRowView key={row.key} row={row} statuses={statuses} />
      ))}
    </ul>
  );

  return (
    <div className="hatch-ambition-groups">
      {[...groups].map(([stage, rows]) => (
        <section key={stage}>
          <h2 className="hatch-ambition-group">
            {stageWord(stage)}, {rows.length}
          </h2>
          {list(rows)}
        </section>
      ))}

      {/* Closed until pressed: finished work is context, not the page. */}
      {finished.length > 0 && (
        <details className="hatch-ambition-finished">
          <summary>{finished.length} finished</summary>
          {list(finished)}
        </details>
      )}
    </div>
  );
}

/** An ambition's stage as a pill, where a row's own column pill would sit. */
function StagePill({ stage }: { stage: AmbitionStage }) {
  return <span className={`hatch-stage-pill hatch-stage-${stage.replace(/ /g, '-')}`}>{stageWord(stage)}</span>;
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

/** One ambition: its head with its stage and what it needs, its capabilities
    as a bar, and its open epics. */
export function AmbitionRowView({ row, statuses }: { row: AmbitionRow; statuses: Status[] }) {
  const drawn = SEGMENTS.map((s) => ({ ...s, n: s.count(row) })).filter((s) => s.n > 0);

  return (
    <Card as="li" className="hatch-ambition-row">
      <div className="hatch-ambition-part">
        <Link to={`/issues/${row.key}`} className="hatch-ambition-title">
          {row.title}
        </Link>
        <span className="hatch-ambition-head-meta">
          <span className="hatch-plan-project">{row.projectKey}</span>
          <StagePill stage={row.stage} />
        </span>
        <p className="hatch-ambition-why">{row.stageWhy}</p>
      </div>

      <div className="hatch-ambition-part">
        <h3 className="hatch-ambition-heading">
          {row.capabilities} {row.capabilities === 1 ? 'capability' : 'capabilities'}
        </h3>
        {drawn.length === 0 ? (
          <EmptyState message="This ambition has no capabilities yet." />
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
          <EmptyState message="No epic is open under this ambition." />
        ) : (
          <>
            <p className="hatch-ambition-tally">
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
                    <span className="hatch-ambition-serves">
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
                  </span>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </Card>
  );
}
