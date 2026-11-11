import { useCallback, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Badge, Card, EmptyState, Field, PageHeader, Table } from '@hatch/ui';
import { getActivityReport, getAssignees, getProjects } from '../api/client';
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
  isValidGap,
  isValidLead,
  kindsWords,
  resolveActor,
  resolveGap,
  resolveLead,
  sessionLengthWords,
  sessionWhenWords,
  touchWords,
  truncatedWords,
  withActor,
  withGap,
  withLead,
} from '../lib/activity';
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
  /** Pinned by tests; the page passes none and gets the reader's. */
  locale?: string;
  /** The Gap/Lead controls, a slot rather than drawn here: this body has a
      standing test asserting it draws nothing that could write. */
  sessionControls?: ReactNode;
}

/**
 * What the activity report draws, once it has loaded: the headline, a row per
 * issue touched, and the trail beneath as evidence. An empty range is a
 * sentence and not an empty table. Pure of the load and the URL, so a test can
 * render it from props.
 */
export function ActivityReportBody({
  actor,
  report,
  offsetMinutes,
  locale,
  sessionControls,
}: ActivityReportBodyProps) {
  if (actor === null) return <EmptyState message={chooseActorWords()} />;
  if (report === null) return <EmptyState message={noProjectWords()} />;
  if (report.total === 0) return <EmptyState message={emptyWords(actor)} />;

  return (
    <>
      <p>
        <strong>{headlineWords(report)}</strong>
      </p>

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
 * (repeated keys), and the window's `from`/`to`/`range`. With no `actor` the
 * page opens on the signed-in person - and writes that name into the URL, so
 * the link does not open on whoever pastes it. An actor the directory does not
 * list is still read, as given: the trail outlives the key it names. Nothing
 * here writes to the board.
 */
function ActivityReportReader({ keys }: { keys: string[] }) {
  const [params, setParams] = useSearchParams();

  const { from, to, now, offsetMinutes, move } = useReportWindow(14);

  const search = params.toString();

  const load = useCallback(async (): Promise<ActivityReportView> => {
    /* The read waits on the directory and the project list rather than going
       out beside them: the URL may name no actor, and holds project keys where
       the read takes ids. */
    const [directory, projects] = await Promise.all([getAssignees(), getProjects()]);
    const query = new URLSearchParams(search);
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
      gap: resolveGap(query),
      lead: resolveLead(query),
    });

    return { report, directory, projects };
  }, [from, to, search, keys]);

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
