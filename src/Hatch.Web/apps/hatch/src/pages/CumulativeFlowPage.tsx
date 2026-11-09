import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { EmptyState, PageHeader } from '@hatch/ui';
import { getCumulativeFlow, getProjects, getStatuses } from '../api/client';
import { CumulativeFlowChart } from '../components/CumulativeFlowChart';
import { Facet } from '../components/Facet';
import { ProjectsFacet } from '../components/ProjectsFacet';
import { isEmpty, resolveColourBy, withColourBy } from '../lib/cumulativeFlow';
import type { ColourBy } from '../lib/cumulativeFlow';
import { makeScale, noProjectWords, reportProjects, unresolvedPhrase, withProjects } from '../lib/timeline';
import { hasMultipleProjects } from '../lib/useProjects';
import { useLoaded } from '../lib/useLoaded';
import { useReportWindow } from '../lib/useReportWindow';
import { useScope } from '../lib/useScope';
import { ReportWindowControls } from '../components/ReportWindowControls';
import type { CumulativeFlow, Project, Status } from '../types';

/** The reads this page is made of, arriving together so the chart is never
    drawn with its colours still to come. */
interface CumulativeFlowView {
  /** `null` when no project is chosen: the read is not made. */
  answer: CumulativeFlow | null;
  statuses: Status[];
  projects: Project[];
}

interface BodyProps {
  answer: CumulativeFlow | null;
  statuses: Status[];
  by: ColourBy;
  projects: Project[];
  offsetMinutes: number;
  now: Date;
  locale?: string;
}

/**
 * What the page shows once the answer is in: the chart, or the sentence that
 * says there is nothing to chart, and under either the footnote for column
 * changes the server could not place. No answer at all - no project chosen -
 * is its own sentence and no footnote.
 *
 * Pure of the load and of the URL, so a test can render it from props - there
 * is no DOM runner to drive `useLoaded`.
 */
export function CumulativeFlowBody({ answer, statuses, by, projects, offsetMinutes, now, locale }: BodyProps) {
  if (answer === null) return <EmptyState message={noProjectWords()} />;

  const unresolved = unresolvedPhrase(answer.unresolved);

  return (
    <>
      {isEmpty(answer) ? (
        <EmptyState message="Nothing was on the board in this range." />
      ) : (
        /* Scaled to the answer's own `from` and `to` - the clamps the server
           applied - not the window that was asked for, as `TimelineBody` does. */
        <CumulativeFlowChart
          answer={answer}
          statuses={statuses}
          by={by}
          projects={projects}
          scale={makeScale(answer.from, answer.to)}
          offsetMinutes={offsetMinutes}
          now={now}
          locale={locale}
        />
      )}

      {unresolved && <p className="text-muted hatch-timeline-footnote">{unresolved}</p>}
    </>
  );
}

/**
 * How much work sat in each column over time, and where it is piling up: one
 * band per board column, stacked bottom-up, drawn along a time axis.
 *
 * The window comes off the URL's `from` and `to`, or a `range` shorthand, and
 * falls back to the last ninety days; the controls write `from` and `to`. The
 * route takes no creator filter - a column's count is not a fact about any
 * one creator - but the URL also carries `project` (repeated keys), which the
 * Projects control writes and the read is scoped by; absent, every project's
 * read is the one it has always been.
 *
 * Under a scoped tab it offers, reads and filters only the scope's projects.
 * Nothing is fetched until the project list is known, so the first request is
 * already the right one - the reader is a component of its own so that wait is
 * not inside `load`.
 */
export function CumulativeFlowPage() {
  const { keys, loaded } = useScope();

  if (!loaded) return <p className="text-muted">Loading…</p>;

  return <CumulativeFlowReader keys={keys} />;
}

function CumulativeFlowReader({ keys }: { keys: string[] }) {
  const [params, setParams] = useSearchParams();

  const { from, to, now, offsetMinutes, move } = useReportWindow();

  const search = params.toString();

  const load = useCallback(async (): Promise<CumulativeFlowView> => {
    const [statuses, projects] = await Promise.all([getStatuses(), getProjects()]);

    /* The read waits on the project list rather than going out beside it, for
       the reason `TimelinePage`'s own load does: the URL holds keys, and
       `reportProjects` needs to know which of them this Hatch still has. */
    const { chosen, narrowed } = reportProjects(new URLSearchParams(search), projects, keys);

    /* No project chosen makes no read: `narrowed` is `[]` then, and an empty
       `project` list appends nothing, so the server would read every project. */
    if (chosen.length === 0) return { answer: null, statuses, projects };

    const answer = await getCumulativeFlow({
      from,
      to,
      project: narrowed?.map((p) => p.key),
    });

    return { answer, statuses, projects };
  }, [from, to, search, keys]);

  const { data, error } = useLoaded<CumulativeFlowView>(load);

  /* Replace rather than push, as TimelinePage's own chooseProject does: ticking
     a project is not a place somebody wants Back to walk them through. */
  function chooseProjects(chosenKeys: string[]) {
    const { offered } = reportProjects(params, data?.projects ?? [], keys);

    setParams(withProjects(params, chosenKeys, offered.map((p) => p.key)), { replace: true });
  }

  function chooseBy(value: string) {
    setParams(withColourBy(params, value === 'project' ? 'project' : 'column'), { replace: true });
  }

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data) return <p className="text-muted">Loading…</p>;

  const { offered, chosen } = reportProjects(params, data.projects, keys);
  const by = resolveColourBy(params);

  return (
    <div className="hatch-page">
      <PageHeader
        title="Cumulative flow"
        description="How much work sat in each column over time, and where it is piling up."
        className="hatch-timeline-header"
        actions={
          <>
            <ReportWindowControls
              defaultPreset="3m"
              window={{ from, to }}
              now={new Date()}
              offsetMinutes={offsetMinutes}
              onMove={move}
            />
            {hasMultipleProjects(offered) && (
              <div className="hatch-timeline-controls" role="group" aria-label="Filters">
                <ProjectsFacet
                  projects={offered}
                  chosen={chosen.map((p) => p.key)}
                  onChange={chooseProjects}
                />
                <Facet
                  label="Colour by"
                  lit={by === 'project'}
                  value={by}
                  options={[
                    { value: 'column', text: 'Column' },
                    { value: 'project', text: 'Project' },
                  ]}
                  onChange={chooseBy}
                />
              </div>
            )}
          </>
        }
      />

      {error && <p className="text-danger">{error}</p>}

      <CumulativeFlowBody
        answer={data.answer}
        statuses={data.statuses}
        by={by}
        projects={data.projects}
        offsetMinutes={offsetMinutes}
        now={now}
      />
    </div>
  );
}
