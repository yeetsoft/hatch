import { useCallback, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Badge, Card, EmptyState, Field, PageHeader, Table } from '@hatch/ui';
import { getActivityReport, getAssignees, getProjects } from '../api/client';
import { ActivityCalendar } from '../components/ActivityCalendar';
import { ActivityGraph } from '../components/ActivityGraph';
import { Facet } from '../components/Facet';
import { ProjectsFacet } from '../components/ProjectsFacet';
import { ReportWindowControls } from '../components/ReportWindowControls';
import {
  actorOptions,
  chooseActorWords,
  emptyWords,
  estimateHeadlineWords,
  estimateRuleWords,
  headlineWords,
  isPerson,
  isValidGap,
  isValidLead,
  kindsWords,
  resolveActor,
  resolveGap,
  resolveIncludeKeys,
  resolveLead,
  sessionLengthWords,
  sessionWhenWords,
  touchWords,
  truncatedWords,
  withActor,
  withGap,
  withIncludeKeys,
  withLead,
} from '../lib/activity';
import { VIEW, offersCalendar, parseView, withView, type ActivityView } from '../lib/activityCalendar';
import {
  MEASURE,
  WINDOW,
  parseMeasure,
  parseWindow,
  withMeasure,
  withWindow,
  type ActivityMeasure,
} from '../lib/activityGraph';
import { describe } from '../lib/events';
import { noProjectWords, reportProjects, withProjects } from '../lib/timeline';
import { useLoaded } from '../lib/useLoaded';
import { useScope } from '../lib/useScope';
import { hasMultipleProjects } from '../lib/useProjects';
import { useReportWindow } from '../lib/useReportWindow';
import type { ActivityReport, AssigneeDirectory, Project } from '../types';

interface ActivityReportBodyProps {
  /** Whose rundown this is; `null` when nobody is chosen. */
  actor: string | null;
  /** `null` when the read was not made: nobody chosen, or no project. */
  report: ActivityReport | null;
  offsetMinutes: number;
  /** Which of the graph's two measures is drawn. */
  measure: ActivityMeasure;
  /** The rolling window the URL names, or `null` for the server's own. */
  windowDays: number | null;
  /** Bars or Calendar, as the URL names it - read as Bars whenever the window
      is too short to offer Calendar at all. */
  view: ActivityView;
  onMeasure: (measure: ActivityMeasure) => void;
  onWindow: (days: number) => void;
  onView: (view: ActivityView) => void;
  /** Pinned by tests; the page passes none and gets the reader's. */
  locale?: string;
  /** The Gap/Lead controls, a slot rather than drawn here: this body has a
      standing test asserting it draws nothing that could write. */
  sessionControls?: ReactNode;
}

/**
 * What the activity report draws, once it has loaded: the headline, the rate of
 * work over time, a row per issue touched, and the trail beneath as evidence.
 * An empty range is a sentence and not an empty table. Pure of the load and the
 * URL, so a test can render it from props.
 */
export function ActivityReportBody({
  actor,
  report,
  offsetMinutes,
  measure,
  windowDays,
  view,
  onMeasure,
  onWindow,
  onView,
  locale,
  sessionControls,
}: ActivityReportBodyProps) {
  if (actor === null) return <EmptyState message={chooseActorWords()} />;
  if (report === null) return <EmptyState message={noProjectWords()} />;
  if (report.total === 0) return <EmptyState message={emptyWords(actor)} />;

  const canCalendar = offersCalendar(report.from, report.to);
  const effectiveView = canCalendar ? view : 'bars';

  return (
    <>
      <p>
        <strong>{headlineWords(report)}</strong>
      </p>

      <h2>Over time</h2>
      {canCalendar && (
        <div className="hatch-activity-view-toggle" role="group" aria-label="View">
          <button
            type="button"
            className={`hatch-activity-view${effectiveView === 'bars' ? ' active' : ''}`}
            aria-pressed={effectiveView === 'bars'}
            onClick={() => onView('bars')}
          >
            Bars
          </button>
          <button
            type="button"
            className={`hatch-activity-view${effectiveView === 'calendar' ? ' active' : ''}`}
            aria-pressed={effectiveView === 'calendar'}
            onClick={() => onView('calendar')}
          >
            Calendar
          </button>
        </div>
      )}
      {effectiveView === 'calendar' ? (
        <ActivityCalendar report={report} measure={measure} onMeasure={onMeasure} locale={locale} />
      ) : (
        <ActivityGraph
          report={report}
          measure={measure}
          onMeasure={onMeasure}
          windowDays={windowDays ?? report.estimate.windowDays}
          onWindow={onWindow}
          locale={locale}
        />
      )}

      <h2>Rundown</h2>
      <Card flush>
        <Table>
          <thead>
            <tr>
              <th>Issue</th>
              <th>First</th>
              <th>Last</th>
              <th>Did</th>
            </tr>
          </thead>
          <tbody>
            {report.issues.map((row) => (
              <tr key={row.issue.key}>
                <td>
                  <Link to={`/issues/${row.issue.key}`}>{row.issue.key}</Link> {row.issue.title}
                </td>
                <td className="text-muted">{touchWords(row.first, offsetMinutes, locale)}</td>
                <td className="text-muted">{touchWords(row.last, offsetMinutes, locale)}</td>
                <td>{kindsWords(row.kinds)}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      </Card>

      <h2>Trail</h2>
      <Card flush>
        <Table>
          <thead>
            <tr>
              <th>When</th>
              <th>Issue</th>
              <th>What</th>
              <th>Who</th>
              <th>Changed</th>
            </tr>
          </thead>
          <tbody>
            {report.events.map((event) => (
              <tr key={event.id}>
                <td className="text-muted">{touchWords(event.at, offsetMinutes, locale)}</td>
                <td>
                  <Link to={`/issues/${event.issueKey}`}>{event.issueKey}</Link>
                </td>
                <td>
                  <Badge>{event.kind.replaceAll('_', ' ')}</Badge>
                </td>
                <td>{event.viaKey ? `${event.actor} via ${event.viaKey}` : event.actor}</td>
                <td className="text-muted">{describe(event)}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      </Card>
      {report.truncated && <p className="text-muted">{truncatedWords(report)}</p>}

      <h2>Sessions</h2>
      <p>
        <strong>{estimateHeadlineWords(report.estimate)}</strong>
      </p>
      <p className="text-muted">{estimateRuleWords(report.estimate)}</p>
      {sessionControls}
      <Card flush>
        <Table>
          <thead>
            <tr>
              <th>When</th>
              <th>Length</th>
              <th>Events</th>
              <th>Issues</th>
            </tr>
          </thead>
          <tbody>
            {report.sessions.map((session) => (
              <tr key={session.from}>
                <td className="text-muted">{sessionWhenWords(session, offsetMinutes, locale)}</td>
                <td>{sessionLengthWords(session.estimatedMinutes)}</td>
                <td>{session.events}</td>
                <td>
                  {session.issueKeys.map((key, i) => (
                    <span key={key}>
                      {i > 0 && ', '}
                      <Link to={`/issues/${key}`}>{key}</Link>
                    </span>
                  ))}
                </td>
              </tr>
            ))}
          </tbody>
        </Table>
      </Card>
    </>
  );
}

/** The reads this page is made of, arriving together so the picker is never
    drawn before the directory it lists. */
interface ActivityReportView {
  report: ActivityReport | null;
  directory: AssigneeDirectory;
  projects: Project[];
}

const REPORT_ANSWERS = 'What one person or key did, issue by issue, over a range.';

/**
 * What one actor did over a range, issue by issue, with the trail beneath.
 *
 * Everything a pasted link needs is in the URL: `actor` (a name), `project`
 * (repeated keys), the window's `from`/`to`/`range`, the graph's `measure`
 * (`events` or `hours`), rolling `window` (days, 1 to 90), `includeKeys` (`true`,
 * offered only for a person) and `view` (`calendar`, past eight weeks). With no `actor`
 * the page opens on the signed-in person - and writes that name into the URL,
 * so the link does not open on whoever pastes it. An actor the directory does not
 * list is still read, as given: the trail outlives the key it names. Nothing
 * here writes to the board.
 */
function ActivityReportReader({ keys }: { keys: string[] }) {
  const [params, setParams] = useSearchParams();

  const { from, to, offsetMinutes, move } = useReportWindow(14);

  const search = params.toString();

  /* What the read depends on: the address without `measure` or `view`. Both
     measures are in the one answer and `view` picks no answer at all, so
     pressing either button must not fetch again - and `search` itself
     changes with the press. */
  const readSearch = withView(withMeasure(params, 'events'), 'bars').toString();

  const load = useCallback(async (): Promise<ActivityReportView> => {
    /* The read waits on the directory and the project list rather than going
       out beside them: the URL may name no actor, and holds project keys where
       the read takes ids. */
    const [directory, projects] = await Promise.all([getAssignees(), getProjects()]);
    const query = new URLSearchParams(readSearch);
    const { actor } = resolveActor(query, directory);
    const { chosen, narrowed } = reportProjects(query, projects, keys);

    /* Nobody chosen, or no project chosen, makes no read: `narrowed` is `[]`
       in the second case, which the server would take as no filter at all. */
    if (actor === null || chosen.length === 0) return { report: null, directory, projects };

    const report = await getActivityReport({
      actor,
      from,
      to,
      projectId: narrowed?.map((p) => p.id),
      offsetMinutes,
      window: parseWindow(query.get(WINDOW)),
      gap: resolveGap(query),
      lead: resolveLead(query),
      includeKeys: resolveIncludeKeys(query) && isPerson(directory, actor),
    });

    return { report, directory, projects };
  }, [from, to, offsetMinutes, readSearch, keys]);

  const { data, error } = useLoaded<ActivityReportView>(load);

  /* Local text, so a digit typed mid-edit does not write to the URL until the
     control is committed; each resyncs from the URL whenever it changes under
     the input, including from this page's own default-actor write. */
  const [gapText, setGapText] = useState(String(resolveGap(params)));
  const [leadText, setLeadText] = useState(String(resolveLead(params)));

  useEffect(() => {
    setGapText(String(resolveGap(params)));
  }, [params]);

  useEffect(() => {
    setLeadText(String(resolveLead(params)));
  }, [params]);

  function commitGap() {
    const parsed = Number(gapText);
    const current = resolveGap(params);
    if (!isValidGap(parsed)) {
      setGapText(String(current));
      return;
    }
    setGapText(String(parsed));
    if (parsed !== current) setParams(withGap(params, parsed), { replace: true });
  }

  function commitLead() {
    const parsed = Number(leadText);
    const current = resolveLead(params);
    if (!isValidLead(parsed)) {
      setLeadText(String(current));
      return;
    }
    setLeadText(String(parsed));
    if (parsed !== current) setParams(withLead(params, parsed), { replace: true });
  }

  /* The signed-in person, written into the URL once resolved, so the address
     says whose rundown it is. Replace rather than push: the page opened on
     this, and Back should not land on the address without it. */
  const resolved = data ? resolveActor(params, data.directory) : null;
  const defaulted = resolved !== null && !resolved.fromUrl ? resolved.actor : null;
  useEffect(() => {
    if (defaulted !== null) setParams(withActor(new URLSearchParams(search), defaulted), { replace: true });
  }, [defaulted, search, setParams]);

  function chooseMeasure(measure: ActivityMeasure) {
    setParams(withMeasure(params, measure), { replace: true });
  }

  function chooseWindow(days: number) {
    setParams(withWindow(params, days), { replace: true });
  }

  function chooseView(view: ActivityView) {
    setParams(withView(params, view), { replace: true });
  }

  function chooseProjects(picked: string[]) {
    const { offered } = reportProjects(params, data?.projects ?? [], keys);

    setParams(withProjects(params, picked, offered.map((p) => p.key)), { replace: true });
  }

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data) return <p className="text-muted">Loading…</p>;

  const { offered, chosen } = reportProjects(params, data.projects, keys);
  const actor = resolveActor(params, data.directory).actor;

  /* Nobody chosen draws a blank first row, so the picker does not claim
     someone it has not read. */
  const options = actorOptions(data.directory.assignees, actor);
  const includeKeys = resolveIncludeKeys(params);
  const choices = actor === null ? [{ value: '', text: 'Choose…' }, ...options] : options;

  const sessionControls = (
    <div className="hatch-timeline-controls" role="group" aria-label="Session">
      <Field label="Gap" hint="minutes of quiet that ends a session">
        <input
          type="number"
          value={gapText}
          onChange={(e) => setGapText(e.target.value)}
          onBlur={commitGap}
          onKeyDown={(e) => {
            if (e.key === 'Enter') commitGap();
          }}
        />
      </Field>
      <Field label="Lead" hint="minutes added to every session's length">
        <input
          type="number"
          value={leadText}
          onChange={(e) => setLeadText(e.target.value)}
          onBlur={commitLead}
          onKeyDown={(e) => {
            if (e.key === 'Enter') commitLead();
          }}
        />
      </Field>
    </div>
  );

  return (
    <div className="hatch-page">
      <PageHeader
        title="Activity"
        description={REPORT_ANSWERS}
        className="hatch-timeline-header"
        actions={
          <>
            <ReportWindowControls
              defaultPreset="2w"
              window={{ from, to }}
              now={new Date()}
              offsetMinutes={offsetMinutes}
              onMove={move}
            />
            <div className="hatch-timeline-controls" role="group" aria-label="Filters">
              <Facet
                label="Actor"
                lit={actor !== null}
                value={actor ?? ''}
                options={choices}
                onChange={(value) => {
                  if (value !== '') setParams(withActor(params, value), { replace: true });
                }}
              />
              {isPerson(data.directory, actor) && (
                <Facet
                  label="Include my keys"
                  lit={includeKeys}
                  value={includeKeys ? 'on' : 'off'}
                  options={[
                    { value: 'off', text: 'Off' },
                    { value: 'on', text: 'On' },
                  ]}
                  onChange={(value) => setParams(withIncludeKeys(params, value === 'on'), { replace: true })}
                />
              )}
              {hasMultipleProjects(offered) && (
                <ProjectsFacet
                  projects={offered}
                  chosen={chosen.map((p) => p.key)}
                  onChange={chooseProjects}
                />
              )}
            </div>
          </>
        }
      />

      {error && <p className="text-danger">{error}</p>}

      <ActivityReportBody
        actor={actor}
        report={data.report}
        offsetMinutes={offsetMinutes}
        measure={parseMeasure(params.get(MEASURE))}
        windowDays={parseWindow(params.get(WINDOW))}
        view={parseView(params.get(VIEW))}
        onMeasure={chooseMeasure}
        onWindow={chooseWindow}
        onView={chooseView}
        sessionControls={sessionControls}
      />
    </div>
  );
}

export function ActivityReportPage() {
  const { keys, loaded } = useScope();

  if (!loaded) return <p className="text-muted">Loading…</p>;

  return <ActivityReportReader keys={keys} />;
}
