import { useCallback } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { EmptyState, PageHeader, ProjectMark, Table, markVars } from '@hatch/ui';
import { getConsumptionReport, getProjects, getStatuses } from '../api/client';
import { Facet } from '../components/Facet';
import { ProjectsFacet } from '../components/ProjectsFacet';
import { ReportWindowControls } from '../components/ReportWindowControls';
import { TypeGlyph } from '../components/TypeGlyph';
import { boardColumns } from '../lib/columns';
import {
  STATUS,
  TABLE_LIMIT,
  TYPE,
  capWords,
  emptiness,
  humanDuration,
  resolveSort,
  resolveStatusId,
  resolveType,
  rowCost,
  totalsLine,
  withSort,
  withStatusId,
  withType,
  type ConsumptionType,
} from '../lib/consumptionReport';
import { projectLogoUrl } from '../lib/projectLogo';
import { noProjectWords, reportProjects, withProjects } from '../lib/timeline';
import { useLoaded } from '../lib/useLoaded';
import { hasMultipleProjects } from '../lib/useProjects';
import { useReportWindow } from '../lib/useReportWindow';
import { useScope } from '../lib/useScope';
import { compactTokens } from '../lib/workLog';
import type { ConsumptionReport, ConsumptionSort, ConsumptionRow, Project, Status } from '../types';

/** The sortable columns, in the order they are drawn after Sessions, which has
    no sort of its own. */
const COLUMNS: { key: ConsumptionSort; label: string }[] = [
  { key: 'tokens', label: 'Tokens' },
  { key: 'cost', label: 'Notional USD' },
  { key: 'time', label: 'Runner time' },
  { key: 'activity', label: 'Last session' },
];

interface ConsumptionReportBodyProps {
  /** `null` when no project is chosen: the read is not made. */
  report: ConsumptionReport | null;
  projects: Project[];
  statuses: Status[];
  sort: ConsumptionSort;
  onSort: (sort: ConsumptionSort) => void;
  /** Pinned by tests; the page passes none and gets the reader's. */
  locale?: string;
}

/** One issue as a row. The status falls back to its raw id for a column the
    board no longer carries, as `transitionPhrase` does. */
function Row({
  row,
  project,
  statuses,
  locale,
}: {
  row: ConsumptionRow;
  project: Project | undefined;
  statuses: Status[];
  locale?: string;
}) {
  const cost = rowCost(row.totals);

  return (
    <tr>
      <td>
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
            <Link to={`/issues/${row.key}`}>{row.key}</Link>
          </span>
        </div>
      </td>
      <td>{row.title}</td>
      <td className="hatch-consumption-type">
        <TypeGlyph type={row.type} />
      </td>
      <td>{statuses.find((s) => s.id === row.statusId)?.name ?? row.statusId}</td>
      <td>{row.totals.sessions}</td>
      <td>{compactTokens(row.totals.totalTokens)}</td>
      <td title={cost.title}>{cost.text}</td>
      <td>{humanDuration(row.totals.durationMs)}</td>
      <td>{new Date(row.lastSessionAt).toLocaleString(locale)}</td>
    </tr>
  );
}

/**
 * What the consumption report draws, once it has loaded: the totals over the
 * whole filter, a note when the table holds only the first of them, and a row
 * per issue in the server's own order - never re-sorted here, which would rank
 * a capped sample. Pure of the load and the URL, so a test can render it from
 * props.
 */
export function ConsumptionReportBody({
  report,
  projects,
  statuses,
  sort,
  onSort,
  locale,
}: ConsumptionReportBodyProps) {
  if (report === null) return <EmptyState message={noProjectWords()} />;

  const empty = emptiness(report, locale);
  if (empty) return <EmptyState message={empty.text} />;

  const byKey = new Map(projects.map((project) => [project.key, project]));
  const cap = capWords(report.matched, report.rows.length);

  return (
    <>
      <p>{totalsLine(report)}</p>
      {cap && <p className="text-muted">{cap}</p>}

      <Table scroll>
        <thead>
          <tr>
            <th scope="col">Project</th>
            <th scope="col">Title</th>
            <th scope="col">Type</th>
            <th scope="col">Status</th>
            <th scope="col">Sessions</th>
            {COLUMNS.map((column) => (
              <th
                key={column.key}
                scope="col"
                /* Descending is the only direction the endpoint offers. */
                aria-sort={sort === column.key ? 'descending' : 'none'}
              >
                <button
                  type="button"
                  className={`hatch-leaderboard-sort${sort === column.key ? ' active' : ''}`}
                  onClick={() => onSort(column.key)}
                >
                  {column.label}
                </button>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {report.rows.map((row) => (
            <Row
              key={row.key}
              row={row}
              project={byKey.get(row.projectKey)}
              statuses={statuses}
              locale={locale}
            />
          ))}
        </tbody>
      </Table>
    </>
  );
}

/** The reads that draw the body, arriving together so it is never drawn with
    its project marks still to come. */
interface ConsumptionReportView {
  report: ConsumptionReport | null;
  projects: Project[];
}

const REPORT_ANSWERS = 'What each epic consumed - tokens, notional dollars and runner time - and how that is trending.';

const TYPE_OPTIONS: { value: ConsumptionType; text: string }[] = [
  { value: 'epic', text: 'Epic' },
  { value: 'reflection', text: 'Reflection' },
  { value: 'story', text: 'Story' },
  { value: 'task', text: 'Task' },
  { value: 'bug', text: 'Bug' },
  { value: 'all', text: 'All' },
];

/**
 * What each issue of a type consumed over a window.
 *
 * The window comes off the URL's `from` and `to`, or a `range` shorthand, and
 * falls back to the last ninety days; the controls write those, and `project`
 * (repeated keys), `type`, `status` (a column's id) and `sort`, each leaving its
 * default out of the URL. A `project` outside the tab's scope is ignored, not
 * rewritten.
 */
function ConsumptionReportReader({ keys }: { keys: string[] }) {
  const [params, setParams] = useSearchParams();

  const { from, to, now, offsetMinutes, move } = useReportWindow(90);

  const search = params.toString();

  const load = useCallback(async (): Promise<ConsumptionReportView> => {
    /* The read waits on the project list rather than going out beside it: the
       URL holds keys, the read takes ids. */
    const projects = await getProjects();
    const query = new URLSearchParams(search);
    const { chosen, narrowed } = reportProjects(query, projects, keys);

    /* No project chosen makes no read: `narrowed` is `[]` then, which the
       server would take as no filter at all. */
    if (chosen.length === 0) return { report: null, projects };

    const statusId = resolveStatusId(query);
    const report = await getConsumptionReport({
      from,
      to,
      type: resolveType(query),
      sort: resolveSort(query),
      limit: TABLE_LIMIT,
      offsetMinutes,
      statusId: statusId ? [statusId] : undefined,
      projectId: narrowed?.map((p) => p.id),
    });

    return { report, projects };
  }, [from, to, search, offsetMinutes, keys]);

  const { data, error } = useLoaded<ConsumptionReportView>(load);

  /* Its own read, on the module-level `getStatuses` whose reference is stable -
     so it loads once, and a changed filter refetches the report and not the
     columns. */
  const { data: statuses } = useLoaded<Status[]>(getStatuses);

  /* Replace rather than push, as the other reports do: ticking a control is not
     a place somebody wants Back to walk them through. */
  function chooseProjects(picked: string[]) {
    const { offered } = reportProjects(params, data?.projects ?? [], keys);

    setParams(withProjects(params, picked, offered.map((p) => p.key)), { replace: true });
  }

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data) return <p className="text-muted">Loading…</p>;

  const { offered, chosen } = reportProjects(params, data.projects, keys);
  const type = resolveType(params);
  const sort = resolveSort(params);
  const columns = boardColumns(statuses ?? []);
  const statusId = resolveStatusId(params);
  /* A pasted `status` the board has no column for is every column, in the
     control as in the read - it would otherwise show a value nothing offers. */
  const status = columns.find((c) => c.id === statusId) ? String(statusId) : '';

  return (
    <div className="hatch-page">
      <PageHeader
        title="Consumption"
        description={REPORT_ANSWERS}
        className="hatch-timeline-header"
        actions={
          <>
            <ReportWindowControls
              defaultPreset="3m"
              window={{ from, to }}
              now={now}
              offsetMinutes={offsetMinutes}
              onMove={move}
            />
            <div className="hatch-timeline-controls" role="group" aria-label="Filters">
              {hasMultipleProjects(offered) && (
                <ProjectsFacet
                  projects={offered}
                  chosen={chosen.map((p) => p.key)}
                  onChange={chooseProjects}
                />
              )}
              <Facet
                label="Type"
                lit={type !== 'epic'}
                value={type}
                options={TYPE_OPTIONS}
                onChange={(value) =>
                  setParams(withType(params, resolveType(new URLSearchParams({ [TYPE]: value }))), {
                    replace: true,
                  })
                }
              />
              <Facet
                label="Status"
                lit={status !== ''}
                value={status}
                options={[
                  { value: '', text: 'Any' },
                  ...columns.map((c) => ({ value: String(c.id), text: c.name })),
                ]}
                onChange={(value) =>
                  setParams(withStatusId(params, resolveStatusId(new URLSearchParams({ [STATUS]: value }))), {
                    replace: true,
                  })
                }
              />
            </div>
          </>
        }
      />

      {error && <p className="text-danger">{error}</p>}

      <ConsumptionReportBody
        report={data.report}
        projects={data.projects}
        statuses={statuses ?? []}
        sort={sort}
        onSort={(next) => setParams(withSort(params, next), { replace: true })}
      />
    </div>
  );
}

export function ConsumptionReportPage() {
  const { keys, loaded } = useScope();

  if (!loaded) return <p className="text-muted">Loading…</p>;

  return <ConsumptionReportReader keys={keys} />;
}
