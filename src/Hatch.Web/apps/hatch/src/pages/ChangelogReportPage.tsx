import { Fragment } from 'react';
import { Link } from 'react-router-dom';
import { Card, EmptyState, Grid, ProjectMark, markVars } from '@hatch/ui';
import { TypeGlyph } from '../components/TypeGlyph';
import { fixLine } from '../lib/changelog';
import {
  bucketLabel,
  changelogReportEmptyWords,
  hasQuietGap,
  quietGapWords,
  type ReportBucketSize,
} from '../lib/changelogReport';
import { beneathLabel } from '../lib/overnight';
import { projectLogoUrl } from '../lib/projectLogo';
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
 * One closed epic as a card, as ChangelogPage's FeatureCard draws a feature.
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
