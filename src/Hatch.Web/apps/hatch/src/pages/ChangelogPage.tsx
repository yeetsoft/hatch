import { useCallback, type MouseEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Card, EmptyState, Grid, PageHeader, ProjectMark, markVars } from '@hatch/ui';
import { getChangelog } from '../api/client';
import { ProseClamp } from '../components/ProseClamp';
import { TypeGlyph } from '../components/TypeGlyph';
import { docLinkTarget, isPlainPrimaryClick } from '../lib/docs';
import { changelogEmptyWords, dayHeading, fixLine } from '../lib/changelog';
import { beneathLabel } from '../lib/overnight';
import { projectLogoUrl } from '../lib/projectLogo';
import { useLoaded } from '../lib/useLoaded';
import { useProjects } from '../lib/useProjects';
import type { ChangelogDto, ChangelogEntryDto, Project } from '../types';

interface ChangelogBodyProps {
  changelog: ChangelogDto;
  projects: Project[];
}

/**
 * One feature's card: its key, title, project mark (when the project still
 * exists), its release note, and how much sat beneath it - the OvernightPage
 * idiom, drawn here instead of a table row.
 */
function FeatureCard({ entry, project }: { entry: ChangelogEntryDto; project: Project | undefined }) {
  const navigate = useNavigate();

  /* Mirrors DocsArticle's onBodyClick: a release-note link into the docs goes
     through the router, with no page reload; anything else is the browser's. */
  const onNoteClick = (e: MouseEvent<HTMLElement>) => {
    const a = (e.target as Element).closest?.('a');
    if (!a || !e.currentTarget.contains(a) || !isPlainPrimaryClick(e)) return;
    if ((a.target && a.target !== '_self') || a.hasAttribute('download')) return;
    const t = docLinkTarget(a.getAttribute('href'));
    if (t.kind !== 'app') return;
    e.preventDefault();
    navigate(t.to);
  };

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
          <Link to={`/issues/${entry.key}`}>{entry.key}</Link>
        </span>
      </div>
      <p>{entry.title}</p>
      {entry.releaseNote && (
        <div onClick={onNoteClick}>
          <ProseClamp source={entry.releaseNote} />
        </div>
      )}
      <p className="text-muted">{beneathLabel(entry.descendantCount)}</p>
    </Card>
  );
}

/**
 * What the changelog draws, once the window has loaded: a heading per day,
 * features above fixes within it, in the server's own order - nothing
 * re-sorted here. Pure of the load, so a test can render it from props.
 */
export function ChangelogBody({ changelog, projects }: ChangelogBodyProps) {
  const byKey = new Map(projects.map((project) => [project.key, project]));

  if (changelog.days.length === 0) {
    return <EmptyState message={changelogEmptyWords()} />;
  }

  return (
    <>
      {changelog.days.map((day) => (
        <section key={day.date} className="hatch-changelog-day">
          <h2>{dayHeading(day.date)}</h2>
          {day.features.length > 0 && (
            <Grid cols={3}>
              {day.features.map((entry) => (
                <FeatureCard key={entry.key} entry={entry} project={byKey.get(entry.projectKey)} />
              ))}
            </Grid>
          )}
          <ul className="hatch-changelog-fixes">
            {day.fixes.map((entry) => (
              <li key={entry.key}>
                <TypeGlyph type={entry.type} />
                {fixLine(entry)}
              </li>
            ))}
          </ul>
        </section>
      ))}
    </>
  );
}

/**
 * What shipped, by day: the newest window the endpoint answers by default -
 * today, the newest fourteen non-empty days, every project. Paging the window
 * further back is HA-1068.
 */
export function ChangelogPage() {
  const load = useCallback(() => getChangelog({}), []);
  const { data, error } = useLoaded<ChangelogDto>(load);
  const { projects } = useProjects();

  return (
    <div className="hatch-page">
      <PageHeader title="Changelog" description="What shipped, by day." />

      {error && <p className="text-danger">{error}</p>}

      {data && <ChangelogBody changelog={data} projects={projects} />}
    </div>
  );
}
