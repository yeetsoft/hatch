import { useEffect, useRef, useState, type MouseEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Button, Card, EmptyState, Grid, PageHeader, ProjectMark, markVars } from '@hatch/ui';
import { getChangelog } from '../api/client';
import { ProseClamp } from '../components/ProseClamp';
import { TypeGlyph } from '../components/TypeGlyph';
import { docLinkTarget, isPlainPrimaryClick } from '../lib/docs';
import {
  appendWindow,
  canLoadOlder,
  changelogEmptyWords,
  dayHeading,
  fixLine,
  monthBefore,
  monthLabel,
  nextBefore,
  parseBefore,
} from '../lib/changelog';
import { message } from '../lib/errors';
import { beneathLabel } from '../lib/overnight';
import { projectLogoUrl } from '../lib/projectLogo';
import { useProjects } from '../lib/useProjects';
import type { ChangelogDto, ChangelogEntryDto, Project } from '../types';

interface ChangelogBodyProps {
  changelog: ChangelogDto;
  projects: Project[];
  /** Fetch the window below the days held. Offered only while the answer says there is one. */
  onOlder: () => void;
  /** The window below is on its way. */
  olderLoading?: boolean;
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
export function ChangelogBody({ changelog, projects, onOlder, olderLoading }: ChangelogBodyProps) {
  const byKey = new Map(projects.map((project) => [project.key, project]));

  if (changelog.days.length === 0) {
    return <EmptyState message={changelogEmptyWords()} />;
  }

  return (
    <>
      {changelog.months.length > 0 && (
        <ul className="hatch-changelog-months">
          {changelog.months.map((m) => (
            <li key={m.month}>
              <Button as={Link} to={`/changelog?before=${monthBefore(m.month)}`}>
                {monthLabel(m)}
              </Button>
            </li>
          ))}
        </ul>
      )}
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
      {canLoadOlder(changelog) && (
        <div className="hatch-changelog-older">
          <Button onClick={onOlder} loading={olderLoading}>
            Older
          </Button>
        </div>
      )}
    </>
  );
}

/**
 * What shipped, by day: a window of the newest days the endpoint answers, with
 * **Older** appending the window below it and a list of months to jump to.
 *
 * The address bar's `?before=` is the request key of the last window loaded, so
 * a shared link or a reload lands on the position the reader had reached. An
 * **Older** press writes it after the fact (`replace`, so Back does not walk the
 * windows); any other change to it - a month press, a pasted link - starts over
 * with one window for that `before`. One request per window, never per day.
 *
 * Not `useLoaded`: that reloads on focus and replaces its data, which could move
 * the first window's `oldest` from under the windows appended below it.
 */
export function ChangelogPage() {
  const [params, setParams] = useSearchParams();
  const before = parseBefore(params);
  const { projects } = useProjects();

  const [held, setHeld] = useState<{ before: string | null; changelog: ChangelogDto } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [olderLoading, setOlderLoading] = useState(false);

  // The latest `held`, for the effect, which must read it without re-running on it.
  const heldRef = useRef(held);
  useEffect(() => {
    heldRef.current = held;
  });
  // Bumped by every request; an answer for an older one is dropped. A new
  // `before` starts a request, so it also drops an Older still on its way.
  const requests = useRef(0);

  useEffect(() => {
    // The URL echo of an Older press: those windows are already held.
    if (heldRef.current && heldRef.current.before === before) return;

    const ticket = ++requests.current;
    setHeld(null);
    setError(null);
    setOlderLoading(false);
    getChangelog({ before }).then(
      (changelog) => {
        if (ticket === requests.current) setHeld({ before, changelog });
      },
      (err) => {
        if (ticket === requests.current) setError(message(err));
      },
    );
  }, [before]);

  function older() {
    const next = held && nextBefore(held.changelog);
    if (!held || !next || olderLoading) return;

    const ticket = ++requests.current;
    setOlderLoading(true);
    setError(null);
    getChangelog({ before: next }).then(
      (answer) => {
        if (ticket !== requests.current) return;
        setHeld({ before: next, changelog: appendWindow(held.changelog, answer) });
        setOlderLoading(false);
        const written = new URLSearchParams(params);
        written.set('before', next);
        setParams(written, { replace: true });
      },
      (err) => {
        if (ticket !== requests.current) return;
        setError(message(err));
        setOlderLoading(false);
      },
    );
  }

  return (
    <div className="hatch-page">
      <PageHeader title="Changelog" description="What shipped, by day." />

      {error && <p className="text-danger">{error}</p>}

      {held && (
        <ChangelogBody
          changelog={held.changelog}
          projects={projects}
          onOlder={older}
          olderLoading={olderLoading}
        />
      )}
    </div>
  );
}
