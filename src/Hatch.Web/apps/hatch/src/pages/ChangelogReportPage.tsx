import { Fragment, useCallback } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Card, EmptyState, Grid, PageHeader, ProjectMark, markVars } from '@hatch/ui';
import { getChangelogReport, getProjects } from '../api/client';
import { CopyMarkdownButton } from '../components/CopyMarkdownButton';
import { Facet } from '../components/Facet';
import { ProjectsFacet } from '../components/ProjectsFacet';
import { ReportWindowControls } from '../components/ReportWindowControls';
import { TypeGlyph } from '../components/TypeGlyph';
import { fullHref } from '../lib/basename';
import { fixLine } from '../lib/changelog';
import {
  BUCKET,
  bucketLabel,
  changelogReportEmptyWords,
  hasQuietGap,
  quietGapWords,
  resolveBucket,
  resolveLevel,
  resolveLoose,
  withBucket,
  withLevel,
  withLoose,
  type ReportBucketSize,
} from '../lib/changelogReport';
import { changelogMarkdown } from '../lib/changelogMarkdown';
import { beneathLabel } from '../lib/overnight';
import { projectLogoUrl } from '../lib/projectLogo';
import { scopedPath } from '../lib/scope';
import { resolveProjects, withProjects } from '../lib/timeline';
import { useLoaded } from '../lib/useLoaded';
import { useScope } from '../lib/useScope';
import { hasMultipleProjects } from '../lib/useProjects';
import { useReportWindow } from '../lib/useReportWindow';
import type { ChangelogReport, ChangelogReportEntry, ChangelogReportGroup, Project } from '../types';

interface ChangelogReportBodyProps {
  report: ChangelogReport;
  projects: Project[];
  level: 'epic' | 'story';
  /** Whether to draw the work under no epic beside each bucket. */
  loose: boolean;
  offsetMinutes: number;
  /** Pinned by tests; the page passes none and gets the reader's. */
  locale?: string;
}

/**
 * One closed issue as a line: its type, key and one-liner. The tint of a board
 * card's priority inset and nothing else of `hatch-card`, which is draggable.
 */
function EntryRow({ entry }: { entry: ChangelogReportEntry }) {
  const emergency = entry.priority === 'emergency';

  return (
    <li
      className={`hatch-changelog-report-entry${emergency ? ' emergency' : ''}`}
      title={emergency ? 'Emergency - the highest priority, first above everything expedited' : undefined}
    >
      <TypeGlyph type={entry.type} />
      <Link to={`/issues/${entry.key}`}>{entry.key}</Link>
      <span>{fixLine(entry)}</span>
    </li>
  );
}

/**
 * One closed epic as a card.
 * Title only: an epic group carries no release note.
 */
function GroupCard({ group, project }: { group: ChangelogReportGroup; project: Project | undefined }) {
  return (
    <Card>
      <div className="hatch-card-head">
        {project && (
          <ProjectMark
            size="sm"
            letters={project.key}
            color={project.color}
            icon={project.icon}
            logoUrl={projectLogoUrl(project)}
            title={project.name}
          />
        )}
        <span
          className={`hatch-card-key${project ? ' hatch-card-key--tinted' : ''}`}
          style={project ? markVars(project.color) : undefined}
        >
          <Link to={`/issues/${group.epic.key}`}>{group.epic.key}</Link>
        </span>
      </div>
      <p>{group.epic.title}</p>
      <p className="text-muted">{beneathLabel(group.descendantCount)}</p>
    </Card>
  );
}

/**
 * What the changelog report draws, once it has loaded: a section per bucket in
 * the server's own order (newest first, nothing re-sorted here), a note where
 * the buckets leave a gap, and at story level the stories under each epic.
 * Pure of the load and the URL, so a test can render it from props.
 */
export function ChangelogReportBody({
  report,
  projects,
  level,
  loose,
  offsetMinutes,
  locale,
}: ChangelogReportBodyProps) {
  const byKey = new Map(projects.map((project) => [project.key, project]));

  if (report.buckets.length === 0) {
    return <EmptyState message={changelogReportEmptyWords()} />;
  }

  return (
    <>
      {report.buckets.map((bucket, i) => {
        const previous = i > 0 ? report.buckets[i - 1] : null;

        return (
          <Fragment key={bucket.start}>
            {previous && hasQuietGap(previous, bucket) && (
              <p className="text-muted hatch-changelog-report-gap">
                {quietGapWords(previous, bucket, offsetMinutes, locale)}
              </p>
            )}
            <section className="hatch-changelog-day">
              <h2>{bucketLabel(bucket.start, report.bucket as ReportBucketSize, offsetMinutes, locale)}</h2>
              {level === 'epic' ? (
                <Grid cols={3}>
                  {bucket.groups.map((group) => (
                    <GroupCard key={group.epic.key} group={group} project={byKey.get(group.epic.projectKey)} />
                  ))}
                </Grid>
              ) : (
                bucket.groups.map((group) => (
                  <div key={group.epic.key}>
                    <h3>
                      <Link to={`/issues/${group.epic.key}`}>{group.epic.title}</Link>
                      {group.closedHere && <span className="hatch-changelog-report-done">Completed</span>}
                    </h3>
                    {group.stories.length > 0 && (
                      <ul className="hatch-changelog-fixes">
                        {group.stories.map((entry) => (
                          <EntryRow key={entry.key} entry={entry} />
                        ))}
                      </ul>
                    )}
                  </div>
                ))
              )}
              {loose && bucket.loose.length > 0 && (
                <>
                  <hr />
                  <h4>Also landed</h4>
                  <ul className="hatch-changelog-fixes">
                    {bucket.loose.map((entry) => (
                      <EntryRow key={entry.key} entry={entry} />
                    ))}
                  </ul>
                </>
              )}
            </section>
          </Fragment>
        );
      })}
    </>
  );
}

/** The reads this page is made of, arriving together so the body is never
    drawn with its project marks still to come. */
interface ChangelogReportView {
  report: ChangelogReport;
  projects: Project[];
}

const REPORT_ANSWERS = 'What landed in each day, week or month, at the level a stakeholder reads.';

/**
 * What landed, bucket by bucket, at the level a stakeholder reads.
 *
 * The window comes off the URL's `from` and `to`, or a `range` shorthand, and
 * falls back to the last two weeks; the controls write those, and `project`
 * (repeated keys), `bucket`, `level` and `loose`, each leaving its default out
 * of the URL.
 */
export function ChangelogReportPage() {
  const [params, setParams] = useSearchParams();
  const { keys } = useScope();

  const { from, to, now, offsetMinutes, move } = useReportWindow(14);

  const search = params.toString();

  const load = useCallback(async (): Promise<ChangelogReportView> => {
    /* The read waits on the project list rather than going out beside it: the
       URL holds keys, the read takes ids, and `resolveProjects` says which of
       those keys this Hatch still has. */
    const projects = await getProjects();
    const query = new URLSearchParams(search);
    const chosen = resolveProjects(query, projects);
    const report = await getChangelogReport({
      from,
      to,
      projectId: chosen.length < projects.length ? chosen.map((p) => p.id) : undefined,
      bucket: resolveBucket(query),
      level: resolveLevel(query),
      loose: resolveLoose(query),
      offsetMinutes,
    });

    return { report, projects };
  }, [from, to, search, offsetMinutes]);

  const { data, error } = useLoaded<ChangelogReportView>(load);

  /* Replace rather than push, as the other reports do: ticking a control is not
     a place somebody wants Back to walk them through. */
  function chooseProjects(keys: string[]) {
    setParams(withProjects(params, keys, (data?.projects ?? []).map((p) => p.key)), { replace: true });
  }

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data) return <p className="text-muted">Loading…</p>;

  const chosen = resolveProjects(params, data.projects);
  const bucket = resolveBucket(params);
  const level = resolveLevel(params);
  const loose = resolveLoose(params);

  // The paste reaches the board the reader was on, on either hostname: the
  // links are built from the page's own location, as Copy link is.
  const markdown = changelogMarkdown(data.report, {
    bucketLabel: (start) => bucketLabel(start, data.report.bucket as ReportBucketSize, offsetMinutes),
    issueUrl: (key) => fullHref(window.location.origin, window.location.pathname, scopedPath('/issues/' + key, keys)),
  });

  return (
    <div className="hatch-page">
      <PageHeader
        title="Changelog"
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
              {hasMultipleProjects(data.projects) && (
                <ProjectsFacet
                  projects={data.projects}
                  chosen={chosen.map((p) => p.key)}
                  onChange={chooseProjects}
                />
              )}
              <Facet
                label="Bucket"
                lit={bucket !== 'day'}
                value={bucket}
                options={[
                  { value: 'hour', text: 'Hour' },
                  { value: 'day', text: 'Day' },
                  { value: 'week', text: 'Week' },
                  { value: 'month', text: 'Month' },
                ]}
                onChange={(value) =>
                  setParams(withBucket(params, resolveBucket(new URLSearchParams({ [BUCKET]: value }))), {
                    replace: true,
                  })
                }
              />
              <Facet
                label="Level"
                lit={level !== 'story'}
                value={level}
                options={[
                  { value: 'epic', text: 'Epic' },
                  { value: 'story', text: 'Story' },
                ]}
                onChange={(value) =>
                  setParams(withLevel(params, value === 'epic' ? 'epic' : 'story'), { replace: true })
                }
              />
              <Facet
                label="Loose"
                lit={loose}
                value={loose ? 'on' : 'off'}
                options={[
                  { value: 'off', text: 'Off' },
                  { value: 'on', text: 'On' },
                ]}
                onChange={(value) => setParams(withLoose(params, value === 'on'), { replace: true })}
              />
            </div>
            <CopyMarkdownButton markdown={markdown} />
          </>
        }
      />

      {error && <p className="text-danger">{error}</p>}

      <ChangelogReportBody
        report={data.report}
        projects={data.projects}
        level={level}
        loose={loose}
        offsetMinutes={offsetMinutes}
      />
    </div>
  );
}
