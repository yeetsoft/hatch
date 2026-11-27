import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, Card, EmptyState, PageHeader } from '@hatch/ui';
import { getAmbitionChanges, getAmbitions, getBallot, getStatuses, putAmbitionsVisit } from '../api/client';
import { AmbitionChangesSection } from '../components/AmbitionChanges';
import { CapabilityBar, StagePill, stageWord } from '../components/AmbitionHeader';
import { AmbitionPresses } from '../components/AmbitionPresses';
import { NewAmbitionDialog } from '../components/NewAmbitionDialog';
import { ScopeGate } from '../components/ScopeGate';
import { StatusPill } from '../components/StatusPill';
import { holdFirstRead, shouldRecord, shownBasis, sinceToSend, type HeldVisit } from '../lib/ambitionVisit';
import { waitedWords } from '../lib/attention';
import { firstLine } from '../lib/dreams';
import { rowBudgetLine } from '../lib/budget';
import { message } from '../lib/errors';
import { scopedPath } from '../lib/scope';
import { useLoaded } from '../lib/useLoaded';
import { useMe } from '../lib/useMe';
import { useScope } from '../lib/useScope';
import type { AmbitionChanges, AmbitionRow, AmbitionStage, Ballot, BallotNode, Question, Status } from '../types';

/** The reads this page is made of, arriving together so a pill is never drawn
    without the colour of its column. The ballot and what changed are the
    exceptions in what they do on failure: each is one part of the page, so its
    failure is carried here as a sentence and never rejects the rest. */
export interface AmbitionsView {
  ambitions: AmbitionRow[];
  statuses: Status[];
  ballot: Ballot | null;
  ballotError: string | null;
  changes: AmbitionChanges | null;
  changesError: string | null;
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
  const { me } = useMe();
  const [shaping, setShaping] = useState(false);
  /* What the first drawn read ran from, and whether this mount has recorded its
     visit: see lib/ambitionVisit. */
  const heldRef = useRef<HeldVisit | null>(null);
  const recordedRef = useRef(false);

  /* The scope in force, as one string: `scope.ids` is empty until the
     provider's own project list lands, so a read made before that was made
     unscoped, and this is what tells the two apart. */
  const scopeKey = scope.isScoped ? scope.keys.join(',') : '';

  const load = useCallback(async (): Promise<AmbitionsView> => {
    /* Handlers are attached at the call, so a ballot or a read of what changed
       that fails is caught here and handed to its own part of the page rather
       than blanking the rows. */
    const [ambitions, statuses, read, changed] = await Promise.all([
      getAmbitions(scope.isScoped ? scope.ids : undefined),
      getStatuses(),
      getBallot(scope.isScoped ? scope.ids : undefined).then(
        (b) => ({ ballot: b, failure: null as string | null }),
        (err: unknown) => ({ ballot: null, failure: message(err) }),
      ),
      getAmbitionChanges(scope.isScoped ? scope.ids : undefined, sinceToSend(heldRef.current)).then(
        (c) => ({ changes: { ...c, basis: shownBasis(heldRef.current, c) }, failure: null as string | null }),
        (err: unknown) => ({ changes: null, failure: message(err) }),
      ),
    ]);
    return {
      ambitions,
      statuses,
      ballot: read.ballot,
      ballotError: read.failure,
      changes: changed.changes,
      changesError: changed.failure,
      scopeKey,
    };
  }, [scope.isScoped, scope.ids, scopeKey]);

  const { data, error, reload } = useLoaded<AmbitionsView>(load);

  /* Only a read made for the scope now in force is drawn: until the refetch the
     provider's late list triggers resolves, `data` is the unscoped one. */
  const current = data !== null && scope.loaded && data.scopeKey === scopeKey;

  /* Opening the page is looking at it, so the visit is recorded once what
     changed has been drawn. A write that fails changes nothing on screen: the
     next visit reads from the earlier one. */
  useEffect(() => {
    const changes = data?.changes ?? null;
    if (!changes || !shouldRecord({ current, changes, recorded: recordedRef.current })) return;
    recordedRef.current = true;
    heldRef.current = holdFirstRead(heldRef.current, changes);
    putAmbitionsVisit().catch(() => {});
  }, [current, data]);

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data || !current) return <p className="text-muted">Loading…</p>;

  /* A key is a program, and the server refuses it a shaping; a wall-off Hatch
     reads a browser as the local person, so it is "not a key", never "a
     person". A browser never presents a key: this is a guard only. */
  const onNew = me?.kind === 'key' ? undefined : () => setShaping(true);

  return (
    <>
      <AmbitionsBody
        data={data}
        scopeKeys={scope.isScoped ? scope.keys : []}
        now={new Date()}
        error={error}
        onNew={onNew}
        onMoved={reload}
      />
      <NewAmbitionDialog open={shaping} projects={scope.projects} onClose={() => setShaping(false)} />
    </>
  );
}

/** The page under its gate: the header, the two tiles, the rows, then what changed. Apart from the loading so a test can draw it from props. */
export function AmbitionsBody({
  data,
  scopeKeys,
  now,
  error = null,
  onNew,
  onMoved,
}: {
  data: AmbitionsView;
  scopeKeys: string[];
  now: Date;
  error?: string | null;
  /** Opens the New ambition dialog; left off, no press is drawn. */
  onNew?: () => void;
  /** Reloads the page after Shape it or Start it moves a row. */
  onMoved: () => void;
}) {
  return (
    <div className="hatch-page">
      <PageHeader
        title="Ambitions"
        description="What you are driving at, across every project, each checked against the trunk on every pass."
        actions={onNew && <NewAmbitionButton onClick={onNew} />}
      />

      {error && <p className="text-danger">{error}</p>}

      <AmbitionTiles data={data} scopeKeys={scopeKeys} now={now} />

      <AmbitionList
        ambitions={data.ambitions}
        statuses={data.statuses}
        scopeKeys={scopeKeys}
        onNew={onNew}
        onMoved={onMoved}
      />

      {data.ambitions.length > 0 && (
        <AmbitionChangesSection changes={data.changes} error={data.changesError} scopeKeys={scopeKeys} now={now} />
      )}
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

/** How many open issues the hold questions on the ambitions in scope hold,
    each counted once. A node with hold questions adds the most any one of them
    holds, read from the ballot's own list by question id. Beneath a node that is
    not an ambition nothing adds, since its count covers what lies under it; an
    ambition's own question holds only itself, so the walk goes on beneath it. */
export function heldIssues(ballot: Ballot): number {
  const holdsOf = new Map(ballot.questions.map((q) => [q.question.id, q.holds]));
  let total = 0;

  const walk = (node: BallotNode) => {
    const held = node.questions.filter((q) => q.level === 'hold').map((q) => holdsOf.get(q.id) ?? 1);
    if (held.length > 0) total += Math.max(...held);
    if (held.length > 0 && node.type !== 'ambition') return;
    for (const child of node.children) walk(child);
  };

  for (const root of ballot.roots) if (root.type === 'ambition') walk(root);
  return total;
}

/** The two tiles above the rows: what waits on the reader, and how many
    capabilities hold. Neither is drawn where no ambition is in scope. */
export function AmbitionTiles({
  data,
  scopeKeys,
  now,
}: {
  data: AmbitionsView;
  scopeKeys: string[];
  now: Date;
}) {
  if (data.ambitions.length === 0) return null;

  return (
    <div className="hatch-ambition-tiles">
      <DecisionsTile ballot={data.ballot} error={data.ballotError} scopeKeys={scopeKeys} now={now} />
      <CapabilitiesTile ambitions={data.ambitions} statuses={data.statuses} />
    </div>
  );
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** The hold-level questions on the ambitions in scope: how many, how many
    issues they hold and how long the oldest has waited, then a line for each
    linking to the ballot. One sentence stands in where there are none or the
    read failed. */
export function DecisionsTile({
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
  const heading = <h2 className="hatch-health-label">Waiting on you</h2>;

  if (error || !ballot) {
    return (
      <section className="hatch-health-tile hatch-ambition-tile hatch-ambition-strip">
        {heading}
        <p className="text-danger hatch-ambition-failure">What is waiting on you could not be read: {error ?? 'no answer'}</p>
      </section>
    );
  }

  const lines = holdQuestions(ballot);
  if (lines.length === 0) {
    return (
      <section className="hatch-health-tile hatch-ambition-tile hatch-ambition-strip">
        {heading}
        <EmptyState message="Nothing is waiting on you." />
      </section>
    );
  }

  const asked = lines.map(({ question }) => Date.parse(question.askedAt)).filter((t) => !Number.isNaN(t));
  const oldest = asked.length === 0 ? null : waitedWords(new Date(Math.min(...asked)).toISOString(), now);
  const age = oldest === null ? '' : oldest === 'just asked' ? ' The oldest was just asked.' : ` The oldest has waited ${oldest}.`;

  return (
    <section className="hatch-health-tile hatch-ambition-tile hatch-ambition-strip">
      {heading}
      <p className="hatch-health-value">{plural(lines.length, 'decision', 'decisions')}</p>
      <p className="hatch-ambition-tile-note">
        Holding {plural(heldIssues(ballot), 'open issue', 'open issues')}.{age}
      </p>
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
      <Link to={scopedPath('/ballot', scopeKeys)} className="hatch-ambition-tile-link">
        Open the ballot
      </Link>
    </section>
  );
}

/** How many capabilities hold across the ambitions still being pursued: those
    in a finished or shelved column are left out, so a closed ambition does not
    read as parts done for good. */
export function CapabilitiesTile({ ambitions, statuses }: { ambitions: AmbitionRow[]; statuses: Status[] }) {
  const live = ambitions.filter((row) => {
    const status = statuses.find((s) => s.id === row.statusId);
    return !status?.isTerminal && !status?.isDeferred;
  });
  const sum = (count: (r: AmbitionRow) => number) => live.reduce((total, row) => total + count(row), 0);

  return (
    <section className="hatch-health-tile hatch-ambition-tile hatch-ambition-capabilities-tile">
      <h2 className="hatch-health-label">Capabilities that hold</h2>
      <p className="hatch-health-value">{`${sum((r) => r.holds)} of ${sum((r) => r.capabilities)}`}</p>
      <p className="hatch-ambition-tile-note">
        {sum((r) => r.inFlight)} more in flight. {sum((r) => r.newlyHolding)} newly holding this week.
      </p>
    </section>
  );
}

/** The stages whose ambitions are folded away at the foot of the list. */
const FINISHED: readonly AmbitionStage[] = ['done', 'shelved'];

/** The primary press that opens the New ambition dialog. */
function NewAmbitionButton({ onClick }: { onClick: () => void }) {
  return (
    <Button variant="primary" onClick={onClick}>
      New ambition
    </Button>
  );
}

/** What an ambition is, in two sentences, shown where none is in scope. */
export const NO_AMBITIONS =
  'An ambition is what you are driving at, broken into capabilities that are each checked against the trunk on every pass. A person starts one by saying what they want, and a session shapes it.';

/**
 * The rows under a heading for each stage, then the finished ones folded under
 * one line. Where none is in scope, the two sentences saying what an ambition
 * is, with the press that starts one.
 *
 * The server sends the rows grouped and in order, so this only cuts them where
 * the stage changes and never sorts: a group's place is where its first row is.
 */
export function AmbitionList({
  ambitions,
  statuses,
  scopeKeys = [],
  onNew,
  onMoved,
}: {
  ambitions: AmbitionRow[];
  statuses: Status[];
  /** The projects the tab is narrowed to, for the link to the ballot. */
  scopeKeys?: string[];
  onNew?: () => void;
  /** Reloads the page after Shape it or Start it moves a row. */
  onMoved: () => void;
}) {
  if (ambitions.length === 0) {
    return <EmptyState message={NO_AMBITIONS} action={onNew && <NewAmbitionButton onClick={onNew} />} />;
  }

  const groups = new Map<AmbitionStage, AmbitionRow[]>();
  const finished: AmbitionRow[] = [];
  for (const row of ambitions) {
    if (FINISHED.includes(row.stage)) finished.push(row);
    else groups.set(row.stage, [...(groups.get(row.stage) ?? []), row]);
  }

  const list = (rows: AmbitionRow[]) => (
    <ul className="hatch-ambition-list">
      {rows.map((row) => (
        <AmbitionRowView key={row.key} row={row} statuses={statuses} scopeKeys={scopeKeys} onMoved={onMoved} />
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

/** The column as a pill, or its name as muted text where the list lacks it. */
function ColumnPill({ statusId, statusName, statuses }: { statusId: number; statusName: string; statuses: Status[] }) {
  const status = statuses.find((s) => s.id === statusId);
  return status ? <StatusPill status={status} size="sm" /> : <span className="text-muted">{statusName}</span>;
}

/** One ambition: its head with its stage, what it needs and what it is for, its
    capabilities as a bar, its open epics, and what is on the reader. */
export function AmbitionRowView({
  row,
  statuses,
  scopeKeys = [],
  onMoved,
}: {
  row: AmbitionRow;
  statuses: Status[];
  scopeKeys?: string[];
  /** Reloads the page after Shape it or Start it moves this row. */
  onMoved: () => void;
}) {
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
        <AmbitionPresses row={row} onMoved={onMoved} />
        {row.summary && <p className="hatch-ambition-summary">{row.summary}</p>}
      </div>

      <div className="hatch-ambition-part">
        <h3 className="hatch-ambition-heading">
          {row.capabilities} {row.capabilities === 1 ? 'capability' : 'capabilities'}
        </h3>
        <CapabilityBar row={row} />
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

      <div className="hatch-ambition-part">
        <h3 className="hatch-ambition-heading">On you</h3>
        {row.decisions > 0 ? (
          <p className="hatch-ambition-decisions">
            <span className="hatch-ambition-swatch hatch-ambition-waiting" aria-hidden="true" />
            <Link to={scopedPath('/ballot', scopeKeys)}>{plural(row.decisions, 'decision', 'decisions')}</Link>
          </p>
        ) : (
          <p className="hatch-ambition-none text-muted">No decision waits on you.</p>
        )}
        <p className={row.budget.spent ? 'hatch-ambition-budget hatch-ambition-budget-spent' : 'hatch-ambition-budget'}>
          {rowBudgetLine(row.budget)}
        </p>
      </div>
    </Card>
  );
}
