import { useCallback, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { EmptyState, PageHeader } from '@hatch/ui';
import { getProjects, getStatuses, getTimeline } from '../api/client';
import { ReportWindowControls } from '../components/ReportWindowControls';
import { TimelineChart } from '../components/TimelineChart';
import { TimelineFilters } from '../components/TimelineFilters';
import { TimelineHidden } from '../components/TimelineHidden';
import {
  CREATOR,
  PROJECT,
  makeScale,
  narrowedTo,
  resolveCreator,
  resolveProject,
  foldRows,
  toggled,
  unresolvedPhrase,
  withFilter,
} from '../lib/timeline';
import { projectsInScope } from '../lib/scope';
import { hiddenEntries, readHidden, toggleHidden, writeHidden } from '../lib/timelineHidden';
import { useLoaded } from '../lib/useLoaded';
import { useReportWindow } from '../lib/useReportWindow';
import { useScope } from '../lib/useScope';
import type { Project, Status, Timeline } from '../types';

/** The reads this page is made of, arriving together so a chart is never drawn
    with its colours still to come, nor a filter with its choices. */
interface TimelineView {
  timeline: Timeline;
  statuses: Status[];
  projects: Project[];
}

interface BodyProps {
  timeline: Timeline;
  statuses: Status[];
  offsetMinutes: number;
  now: Date;
  expanded: ReadonlySet<string>;
  onToggle: (key: string) => void;
  hidden: ReadonlySet<string>;
  onHide: (key: string) => void;
}

/**
 * What the page shows once the answer is in: the chart, or the sentence that
 * says there is nothing to chart (or that every row in it is hidden), and under either the footnote for column
 * changes the server could not place.
 *
 * Pure of the load and of the URL, so a test can render it from props - there
 * is no DOM runner to drive `useLoaded`.
 */
export function TimelineBody({ timeline, statuses, offsetMinutes, now, expanded, onToggle, hidden, onHide }: BodyProps) {
  const empty = timeline.epics.length === 0 && timeline.other === null;
  const unresolved = unresolvedPhrase(timeline.unresolved);

  return (
    <>
      {empty ? (
        <EmptyState message="Nothing was active in this range." />
      ) : foldRows(timeline, expanded, hidden).length === 0 ? (
        <EmptyState message="Every epic in this range is hidden." />
      ) : (
        /* Scaled to the answer's own `from` and `to` - the clamps the server
           applied - not the window that was asked for, so the ticks and the
           bars agree with what was actually read. */
        <TimelineChart
          timeline={timeline}
          statuses={statuses}
          scale={makeScale(timeline.from, timeline.to)}
          offsetMinutes={offsetMinutes}
          now={now}
          expanded={expanded}
          onToggle={onToggle}
          hidden={hidden}
          onHide={onHide}
        />
      )}

      {unresolved && <p className="text-muted hatch-timeline-footnote">{unresolved}</p>}
    </>
  );
}

/**
 * The projects a Timeline offers, reads and filters to: `offered`, those in the
 * tab's scope (every project when `keys` is empty); `chosen`, the one the URL's
 * `project` key names among them, `null` for a key outside the scope or naming
 * nothing; and `ids` to send, the chosen project or else the offered ones, left
 * `undefined` when that is every project so nothing is sent.
 *
 * Pure, and used by both the load and the render, so the wiring is testable
 * without a DOM runner to drive `useLoaded`.
 */
export function timelineProjects(projects: Project[], keys: string[], projectKey: string) {
  const offered = projectsInScope(projects, keys);
  const chosen = resolveProject(new URLSearchParams({ [PROJECT]: projectKey }), offered);

  return { offered, chosen, ids: narrowedTo(chosen ? [chosen] : offered, projects)?.map((p) => p.id) };
}

/**
 * When the work happened: every epic as a row of coloured stretches, one per
 * column it sat in, with its children a click away.
 *
 * Under a scoped tab it offers, reads and filters only the scope's projects.
 * Nothing is fetched until the project list is known, so the first request is
 * already the right one - the reader is a component of its own so that wait is
 * not inside `load`.
 */
export function TimelinePage() {
  const { keys, isScoped, loaded } = useScope();

  if (!loaded) return <p className="text-muted">Loading…</p>;

  return <TimelineReader keys={keys} isScoped={isScoped} />;
}

/**
 * What the Timeline holds, within the tab's scope.
 *
 * The window comes off the URL's `from` and `to`, or a `range` shorthand, and
 * falls back to the last ninety days; the controls write `from` and `to`. The
 * URL also carries `project` (a key) and `creator` (a name), which the filters
 * write and the read is scoped by. Which rows are open is page state and never
 * the URL, since a pasted link should open the same range and not somebody's
 * half-unfolded view. A `project` outside the scope is ignored, not rewritten.
 */
function TimelineReader({ keys, isScoped }: { keys: string[]; isScoped: boolean }) {
  const [params, setParams] = useSearchParams();

  const { from, to, now, offsetMinutes, move } = useReportWindow();

  const search = params.toString();
  const { projectKey, creator } = useMemo(() => {
    const url = new URLSearchParams(search);

    return { projectKey: url.get(PROJECT) ?? '', creator: resolveCreator(url) };
  }, [search]);

  const load = useCallback(async (): Promise<TimelineView> => {
    const [statuses, projects] = await Promise.all([getStatuses(), getProjects()]);

    /* The read waits on the project list rather than going out beside it,
       because the URL holds a key - the thing a person would type or paste -
       and the API takes an id. A key outside the scope, or naming nothing,
       falls back to every project in scope, which is what the picker will be
       showing anyway. */
    const { ids } = timelineProjects(projects, keys, projectKey);
    const timeline = await getTimeline({ from, to, projectId: ids, createdBy: creator || null });

    return { timeline, statuses, projects };
  }, [from, to, projectKey, creator, keys]);

  const { data, error } = useLoaded<TimelineView>(load);

  /* Replace rather than push, as `move` does: flipping a filter is not a place
     somebody wants Back to walk them through. The window is left as it is. */
  function chooseProject(key: string) {
    setParams(withFilter(params, PROJECT, key), { replace: true });
  }

  function chooseCreator(name: string) {
    setParams(withFilter(params, CREATOR, name), { replace: true });
  }

  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set());
  const [hidden, setHidden] = useState<ReadonlySet<string>>(() => new Set(readHidden()));

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data) return <p className="text-muted">Loading…</p>;

  const { offered, chosen } = timelineProjects(data.projects, keys, projectKey);

  return (
    <div className="hatch-page">
      <PageHeader
        title="Timeline"
        description="When each epic was in flight, and how long its work sat in each column."
        className="hatch-timeline-header"
        /* The requested window, not the answer's, so the strip responds while a
           refetch is in flight. `now` is fresh rather than the memoised one so a
           tab left open past midnight moves on the next press of Today. */
        actions={
          <>
            <ReportWindowControls
              defaultPreset="3m"
              window={{ from, to }}
              now={new Date()}
              offsetMinutes={offsetMinutes}
              onMove={move}
            />
            <TimelineFilters
              projects={offered}
              project={chosen?.key ?? ''}
              creators={data.timeline.creators}
              creator={creator}
              onProject={chooseProject}
              onCreator={chooseCreator}
              scoped={isScoped}
            />
            <TimelineHidden
              entries={hiddenEntries(data.timeline, [...hidden])}
              onShow={(key) => setHidden(new Set(toggleHidden(key)))}
              onShowAll={() => {
                writeHidden([]);
                setHidden(new Set(readHidden()));
              }}
            />
          </>
        }
      />

      {error && <p className="text-danger">{error}</p>}

      <TimelineBody
        timeline={data.timeline}
        statuses={data.statuses}
        offsetMinutes={offsetMinutes}
        now={now}
        expanded={expanded}
        onToggle={(key) => setExpanded((open) => toggled(open, key))}
        hidden={hidden}
        onHide={(key) => setHidden(new Set(toggleHidden(key)))}
      />
    </div>
  );
}
