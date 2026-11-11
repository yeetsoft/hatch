import { useCallback, useEffect } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Badge, Card, EmptyState, PageHeader, Table } from '@hatch/ui';
import { getActivityReport, getAssignees, getProjects } from '../api/client';
import { ActivityGraph } from '../components/ActivityGraph';
import { Facet } from '../components/Facet';
import { ProjectsFacet } from '../components/ProjectsFacet';
import { ReportWindowControls } from '../components/ReportWindowControls';
import {
  actorOptions,
  chooseActorWords,
  emptyWords,
  headlineWords,
  kindsWords,
  resolveActor,
  touchWords,
  truncatedWords,
  withActor,
} from '../lib/activity';
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
  onMeasure: (measure: ActivityMeasure) => void;
  onWindow: (days: number) => void;
  /** Pinned by tests; the page passes none and gets the reader's. */
  locale?: string;
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
  onMeasure,
  onWindow,
  locale,
}: ActivityReportBodyProps) {
  if (actor === null) return <EmptyState message={chooseActorWords()} />;
  if (report === null) return <EmptyState message={noProjectWords()} />;
  if (report.total === 0) return <EmptyState message={emptyWords(actor)} />;

  return (
    <>
      <p>
        <strong>{headlineWords(report)}</strong>
      </p>

      <h2>Over time</h2>
      <ActivityGraph
        report={report}
        measure={measure}
        onMeasure={onMeasure}
        windowDays={windowDays ?? report.estimate.windowDays}
        onWindow={onWindow}
        locale={locale}
      />

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
                <td>{event.actor}</td>
                <td className="text-muted">{describe(event)}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      </Card>
      {report.truncated && <p className="text-muted">{truncatedWords(report)}</p>}
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
 * (repeated keys), the window's `from`/`to`/`range`, and the graph's `measure`
 * (`events` or `hours`) and rolling `window` (days, 1 to 90). With no `actor`
 * the page opens on the signed-in person - and writes that name into the URL,
 * so the link does not open on whoever pastes it. An actor the directory does not
 * list is still read, as given: the trail outlives the key it names. Nothing
 * here writes to the board.
 */
function ActivityReportReader({ keys }: { keys: string[] }) {
  const [params, setParams] = useSearchParams();

  const { from, to, now, offsetMinutes, move } = useReportWindow(14);

  const search = params.toString();

  /* What the read depends on: the address without `measure`. Both measures are
     in the one answer, so pressing a button must not fetch it again - and
     `search` itself changes with the press. */
  const readSearch = withMeasure(params, 'events').toString();

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
    });

    return { report, directory, projects };
  }, [from, to, offsetMinutes, readSearch, keys]);

  const { data, error } = useLoaded<ActivityReportView>(load);

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
  const choices = actor === null ? [{ value: '', text: 'Choose…' }, ...options] : options;

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
              now={now}
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
        onMeasure={chooseMeasure}
        onWindow={chooseWindow}
      />
    </div>
  );
}

export function ActivityReportPage() {
  const { keys, loaded } = useScope();

  if (!loaded) return <p className="text-muted">Loading…</p>;

  return <ActivityReportReader keys={keys} />;
}
