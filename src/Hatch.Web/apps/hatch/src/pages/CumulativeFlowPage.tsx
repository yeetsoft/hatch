import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { EmptyState, PageHeader } from '@hatch/ui';
import { getCumulativeFlow, getProjects, getStatuses } from '../api/client';
import { CumulativeFlowChart } from '../components/CumulativeFlowChart';
import { ProjectsFacet } from '../components/ProjectsFacet';
import { isEmpty } from '../lib/cumulativeFlow';
import { makeScale, resolveProjects, unresolvedPhrase, withProjects } from '../lib/timeline';
import { hasMultipleProjects } from '../lib/useProjects';
import { useLoaded } from '../lib/useLoaded';
import { useReportWindow } from '../lib/useReportWindow';
import { ReportWindowControls } from '../components/ReportWindowControls';
import type { CumulativeFlow, Project, Status } from '../types';

/** The reads this page is made of, arriving together so the chart is never
    drawn with its colours still to come. */
interface CumulativeFlowView {
  answer: CumulativeFlow;
  statuses: Status[];
  projects: Project[];
}

interface BodyProps {
  answer: CumulativeFlow;
  statuses: Status[];
  offsetMinutes: number;
  now: Date;
  locale?: string;
}

/**
 * What the page shows once the answer is in: the chart, or the sentence that
 * says there is nothing to chart, and under either the footnote for column
 * changes the server could not place.
 *
 * Pure of the load and of the URL, so a test can render it from props - there
 * is no DOM runner to drive `useLoaded`.
 */
export function CumulativeFlowBody({ answer, statuses, offsetMinutes, now, locale }: BodyProps) {
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
 */
export function CumulativeFlowPage() {
  const [params, setParams] = useSearchParams();

  const { from, to, now, offsetMinutes, move } = useReportWindow();

  const search = params.toString();

  const load = useCallback(async (): Promise<CumulativeFlowView> => {
    const [statuses, projects] = await Promise.all([getStatuses(), getProjects()]);

    /* The read waits on the project list rather than going out beside it, for
       the reason `TimelinePage`'s own load does: the URL holds keys, and
       `resolveProjects` needs to know which of them this Hatch still has. */
    const chosen = resolveProjects(new URLSearchParams(search), projects);
    const answer = await getCumulativeFlow({
      from,
      to,
      project: chosen.length < projects.length ? chosen.map((p) => p.key) : undefined,
    });

    return { answer, statuses, projects };
  }, [from, to, search]);

  const { data, error } = useLoaded<CumulativeFlowView>(load);

  /* Replace rather than push, as TimelinePage's own chooseProject does: ticking
     a project is not a place somebody wants Back to walk them through. */
  function chooseProjects(keys: string[]) {
    setParams(withProjects(params, keys, (data?.projects ?? []).map((p) => p.key)), { replace: true });
  }

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data) return <p className="text-muted">Loading…</p>;

  const chosen = resolveProjects(params, data.projects);

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
            {hasMultipleProjects(data.projects) && (
              <div className="hatch-timeline-controls" role="group" aria-label="Filters">
                <ProjectsFacet
                  projects={data.projects}
                  chosen={chosen.map((p) => p.key)}
                  onChange={chooseProjects}
                />
              </div>
            )}
          </>
        }
      />

      {error && <p className="text-danger">{error}</p>}

      <CumulativeFlowBody answer={data.answer} statuses={data.statuses} offsetMinutes={offsetMinutes} now={now} />
    </div>
  );
}
