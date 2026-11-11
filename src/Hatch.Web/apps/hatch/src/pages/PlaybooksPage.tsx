import { useMemo, useState } from 'react';
import { Badge, Button, Card, EmptyState, Field, PageHeader, Table, Text } from '@hatch/ui';
import { Link } from 'react-router-dom';
import {
  approveAllPlaybookProposals,
  deletePlaybook,
  getPlaybookFiles,
  getPlaybookProposals,
  getPlaybooks,
  getStatuses,
  rejectAllPlaybookProposals,
} from '../api/client';
import { PlaybookEditDialog } from '../components/PlaybookEditDialog';
import { PlaybookProposalCard } from '../components/PlaybookProposalCard';
import { PlaybookRow } from '../components/PlaybookRow';
import { StatusPill } from '../components/StatusPill';
import { TypeBadge } from '../components/TypeBadge';
import { agoPhrase } from '../lib/claim';
import { message } from '../lib/errors';
import {
  boardRowNote,
  coverageOf,
  fileRowLabel,
  isReviewPlaybook,
  isUncheckedEdit,
  limitNotes,
  matchesProjectFilter,
  notReadNote,
  playbookLabel,
  transitionLabel,
} from '../lib/playbooks';
import type { CoverageRow } from '../lib/playbooks';
import {
  absentSentence,
  boundCanonicals,
  fileForProject,
  projectsReading,
  shortSha,
  sortedFiles,
  stateLabel,
  visibleFiles,
} from '../lib/playbookFiles';
import {
  approveAllDisabledHint,
  approveAllSuccessSentence,
  proposalsBannerSentence,
  rejectAllSuccessSentence,
} from '../lib/playbookProposals';
import { pullRequestWords } from '../lib/pullRequest';
import { useLoaded } from '../lib/useLoaded';
import { hasMultipleProjects, useProjects } from '../lib/useProjects';
import type {
  Playbook,
  PlaybookFileDto,
  PlaybookFileRowDto,
  PlaybookFilesDto,
  PlaybookProposal,
  Project,
  Status,
} from '../types';

/**
 * The matrix an agent is dispatched by, and the only place it can be changed.
 *
 * Everything on this page is refused to an API key on purpose: a playbook picks
 * the next agent's instructions, its model and its budget, so an agent able to
 * edit one could widen its own. The page is the operator's side of that
 * refusal, and it is why the settings are worth having as rows at all - the
 * right effort for a transition is not something anybody guesses correctly the
 * first time, it is something they retune after watching a run go badly.
 */

/** The notice line Approve all/Reject all leave in the proposals banner. */
export interface PlaybookProposalNotice {
  tone: 'success' | 'danger';
  text: string;
}

export function PlaybooksPage() {
  const { data: playbooks, error, setError, reload } = useLoaded<Playbook[]>(getPlaybooks);
  const { data: statuses } = useLoaded<Status[]>(getStatuses);
  const { data: files } = useLoaded<PlaybookFilesDto>(getPlaybookFiles);
  const { data: proposals, reload: reloadProposals } = useLoaded<PlaybookProposal[]>(getPlaybookProposals);
  const { projects } = useProjects();
  const [projectFilter, setProjectFilter] = useState('');
  const [showOverridden, setShowOverridden] = useState(false);
  const [editing, setEditing] = useState<Playbook | null>(null);
  const [creating, setCreating] = useState(false);
  const [proposalNotice, setProposalNotice] = useState<PlaybookProposalNotice | null>(null);

  const bound = useMemo(() => boundCanonicals(projects), [projects]);
  const visibleFileList = files && sortedFiles(visibleFiles(files.files, bound));

  // Once per render, as ClaimPanel's own `now` is - every card on the page
  // judges "read how long ago" against the same instant.
  const now = new Date();

  const reloadAll = () => Promise.all([reload(), reloadProposals()]);

  async function act(action: () => Promise<unknown>) {
    try {
      await action();
      await reloadAll();
      setError(null);
    } catch (err) {
      setError(message(err));
    }
  }

  async function approveAll() {
    if (!proposals) return;
    try {
      const result = await approveAllPlaybookProposals(proposals.map((p) => p.id));
      await reloadAll();
      setProposalNotice({ tone: 'success', text: approveAllSuccessSentence(result.playbooks.length) });
    } catch (err) {
      await reloadProposals();
      setProposalNotice({ tone: 'danger', text: message(err) });
    }
  }

  async function rejectAll() {
    if (!proposals) return;
    try {
      const result = await rejectAllPlaybookProposals(proposals.map((p) => p.id));
      await reloadAll();
      setProposalNotice({ tone: 'success', text: rejectAllSuccessSentence(result.rejected) });
    } catch (err) {
      await reloadProposals();
      setProposalNotice({ tone: 'danger', text: message(err) });
    }
  }

  const filteredProject = projectFilter ? projects.find((p) => p.key === projectFilter) ?? null : null;
  const activeFile =
    filteredProject?.repoSourced && files ? fileForProject(filteredProject, files.files) : null;

  return (
    <div className="hatch-page">
      <PageHeader
        title="Playbooks"
        description="What an agent is told, and how much thought to spend, when it moves an issue one column along - and, for the review column to itself, when a pull request there has stopped merging with the trunk."
        actions={
          <>
            {hasMultipleProjects(projects) && (
              <Field label="Project">
                <select value={projectFilter} onChange={(e) => setProjectFilter(e.target.value)}>
                  <option value="">All projects</option>
                  {projects.map((project) => (
                    <option key={project.id} value={project.key}>
                      {project.key} — {project.name}
                    </option>
                  ))}
                </select>
              </Field>
            )}
            <Button variant="primary" disabled={!statuses} onClick={() => setCreating(true)}>
              New playbook
            </Button>
          </>
        }
      />

      {error && <p className="text-danger">{error}</p>}

      <PlaybookProposals
        proposals={proposals}
        statuses={statuses ?? []}
        now={now}
        notice={proposalNotice}
        onApproveAll={() => void approveAll()}
        onRejectAll={() => void rejectAll()}
      />

      {playbooks?.length === 0 && (activeFile?.rows.length ?? 0) === 0 && (
        <EmptyState message="No playbooks yet. Without one, a transition dispatches nothing at all - hatch.sh work says so rather than guessing." />
      )}

      {playbooks && (playbooks.length > 0 || (activeFile?.rows.length ?? 0) > 0) && (
        <PlaybooksTable
          playbooks={playbooks}
          activeFile={activeFile}
          filteredProject={filteredProject}
          projects={projects}
          statuses={statuses ?? []}
          showOverridden={showOverridden}
          onToggleOverridden={() => setShowOverridden((v) => !v)}
          onEdit={setEditing}
          onDelete={(p) => void act(() => deletePlaybook(p.id))}
        />
      )}

      <h2 className="hatch-section-title">Files on the trunk</h2>

      {visibleFileList?.length === 0 && (
        <EmptyState message="No .hatch/playbooks.yaml read yet - a runner's poll puts one here, at the root of a trunk some project's repository points at." />
      )}

      {visibleFileList?.map((f) => {
        const readers = projectsReading(f, projects, files?.files ?? []);
        return (
          <Card key={`${f.canonical}:${f.trunk}`}>
            <div className="hatch-playbook-file-head">
              <strong>{f.canonical}</strong>
              <Text tone="muted" as="span">{f.trunk}</Text>
              <code>{shortSha(f.sha)}</code>
              <Badge tone={f.state === 'invalid' ? 'danger' : 'muted'}>{stateLabel(f.state)}</Badge>
              <Text tone="muted" as="span">
                read {agoPhrase(f.checkedAt, now)} by {f.runner}
              </Text>
              {f.url !== null && (
                <a className="hatch-chip hatch-chip-link" href={f.url} target="_blank" rel="noreferrer noopener">
                  <span className="hatch-chip-kind">File</span>
                  {pullRequestWords(f.url)}
                </a>
              )}
            </div>

            {f.state === 'parsed' && (
              <Table>
                <thead>
                  <tr>
                    <th>Transition</th>
                    <th>Types</th>
                    <th>Model</th>
                    <th>Effort</th>
                  </tr>
                </thead>
                <tbody>
                  {f.rows.map((row, i) => (
                    <PlaybookFileRowLine key={`${transitionLabel(row)}-${i}`} row={row} statuses={statuses ?? []} />
                  ))}
                </tbody>
              </Table>
            )}

            {f.state === 'invalid' && (
              <p>
                {f.problem}
                {f.lastGoodSha !== null && <> — last good sha {shortSha(f.lastGoodSha)}</>}
              </p>
            )}

            {f.state === 'absent' && <p>{absentSentence(f.trunk)}</p>}

            {readers.length > 0 ? (
              <Text tone="muted" as="p">{`read by ${readers.map((p) => p.key).join(', ')}`}</Text>
            ) : (
              <Text tone="muted" as="p">no project reads this file yet</Text>
            )}
          </Card>
        );
      })}

      <PlaybookEditDialog
        open={creating || editing !== null}
        playbook={editing}
        statuses={statuses ?? []}
        projects={projects}
        onClose={() => {
          setEditing(null);
          setCreating(false);
        }}
        onSaved={() => void reloadAll()}
      />
    </div>
  );
}

/**
 * The banner and the cards a session's pending proposals draw above the main
 * table - nothing at all when there are none (criterion 1: no empty banner).
 * A card carries no proposal of its own, so every change across every
 * pending proposal is flattened into one list, in proposal order, and the
 * banner's own line per proposal is where its reason, author, ticket and age
 * are read.
 *
 * Pure of the load, so a test can render it from props - there is no DOM
 * runner to drive `useLoaded`.
 */
export function PlaybookProposals({
  proposals,
  statuses,
  now,
  notice = null,
  onApproveAll = () => {},
  onRejectAll = () => {},
}: {
  proposals: PlaybookProposal[] | null;
  statuses: Status[];
  now: Date;
  notice?: PlaybookProposalNotice | null;
  onApproveAll?: () => void;
  onRejectAll?: () => void;
}) {
  const list = proposals ?? [];
  if (list.length === 0 && !notice) return null;

  const disabledHint = approveAllDisabledHint(list);

  return (
    <>
      <Card className="hatch-playbook-proposal-banner">
        {list.length > 0 && (
          <>
            <div className="hatch-playbook-proposal-actions">
              <strong>{proposalsBannerSentence(list)}</strong>
              <Button variant="primary" disabled={disabledHint !== null} title={disabledHint ?? undefined} onClick={onApproveAll}>
                Approve all
              </Button>
              <Button variant="secondary" onClick={onRejectAll}>
                Reject all
              </Button>
            </div>
            {list.map((p) => (
              <Text key={p.id} tone="muted" as="p">
                {p.reason} — {p.author}
                {p.issueKey !== null && (
                  <>
                    {' '}
                    &middot; <Link to={`/issues/${p.issueKey}`}>{p.issueKey}</Link>
                  </>
                )}
                {' '}
                &middot; {agoPhrase(p.createdAt, now)}
              </Text>
            ))}
          </>
        )}
        {notice && <p className={notice.tone === 'danger' ? 'text-danger' : undefined}>{notice.text}</p>}
      </Card>
      {list.length > 0 && (
        <div className="hatch-playbook-proposal-list">
          {list.flatMap((p) => p.changes).map((c) => (
            <PlaybookProposalCard key={c.id} change={c} statuses={statuses} />
          ))}
        </div>
      )}
    </>
  );
}

/**
 * The main table itself: the file's own rows first, then every playbook the
 * project filter keeps, with a `Show N overridden` toggle set aside behind
 * it - the board-wide rows the filtered project's own layer wholly covers,
 * and, for a repo-sourced project, that project's own leftover table rows,
 * which the dispatcher never reads once the project reads a file instead.
 * A partly overridden board-wide row is never set aside - it still runs for
 * something - and carries `boardRowNote`'s words regardless of the toggle.
 *
 * Pure of the load, so a test can render it from props - there is no DOM
 * runner to drive `useLoaded`.
 */
export function PlaybooksTable({
  playbooks,
  activeFile,
  filteredProject,
  projects,
  statuses,
  showOverridden,
  onToggleOverridden,
  onEdit,
  onDelete,
}: {
  playbooks: Playbook[];
  activeFile: PlaybookFileDto | null;
  filteredProject: Project | null;
  projects: Project[];
  statuses: Status[];
  showOverridden: boolean;
  onToggleOverridden: () => void;
  onEdit: (p: Playbook) => void;
  onDelete: (p: Playbook) => void;
}) {
  const ownRows: CoverageRow[] = !filteredProject
    ? []
    : activeFile
      ? activeFile.rows
      : playbooks.filter((p) => p.projectKey === filteredProject.key);

  const ownLabel = filteredProject
    ? filteredProject.repoSourced
      ? 'the file'
      : playbookLabel({ projectKey: filteredProject.key })
    : '';

  function asideInfo(p: Playbook): { note: string | null; aside: boolean } {
    if (!filteredProject) return { note: null, aside: false };
    if (p.projectKey === null) {
      return { note: boardRowNote(p, ownRows, ownLabel), aside: coverageOf(p, ownRows) === 'overridden' };
    }
    if (filteredProject.repoSourced && p.projectKey === filteredProject.key) {
      return { note: notReadNote(p.projectKey), aside: true };
    }
    return { note: null, aside: false };
  }

  const rows = playbooks
    .filter((p) => matchesProjectFilter(p, filteredProject?.key ?? ''))
    .map((p) => ({ playbook: p, ...asideInfo(p) }));
  const asideCount = rows.filter((r) => r.aside).length;

  return (
    <>
      {asideCount > 0 && (
        <Button variant="secondary" onClick={onToggleOverridden}>
          {showOverridden ? `Hide ${asideCount} overridden` : `Show ${asideCount} overridden`}
        </Button>
      )}
      <Card flush>
        <Table>
          <thead>
            <tr>
              <th>Transition</th>
              {hasMultipleProjects(projects) && <th>Project</th>}
              <th>Types</th>
              <th>Model</th>
              <th>Effort</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {activeFile?.rows.map((row, i) => (
              <PlaybookFileRowLine
                key={`${transitionLabel(row)}-${i}`}
                row={row}
                statuses={statuses}
                label={fileRowLabel(activeFile)}
              />
            ))}
            {rows.map(({ playbook, note, aside }) => {
              if (aside && !showOverridden) return null;
              return (
                <PlaybookRow
                  key={playbook.id}
                  playbook={playbook}
                  projects={projects}
                  statuses={statuses}
                  note={note}
                  dimmed={aside}
                  onEdit={() => onEdit(playbook)}
                  onDelete={() => onDelete(playbook)}
                />
              );
            })}
          </tbody>
        </Table>
      </Card>
    </>
  );
}

/** One row of a file's parsed playbook, read-only: the main table's own
    `PlaybookRow` minus the edit button and Delete - a file row's own fields
    may not be changed. `label`, given once a project filter merges this row
    into the main table, draws in the same column `PlaybookRow` draws its own
    label in, with a trailing blank cell standing in for the Delete column a
    file row never has; omitted, as in the "Files on the trunk" card below,
    neither cell is drawn and the row is this mini-table's own four columns. */
export function PlaybookFileRowLine({
  row,
  statuses,
  label,
}: {
  row: PlaybookFileRowDto;
  statuses: Status[];
  label?: string;
}) {
  const fromStatus = statuses.find((s) => s.id === row.fromStatusId);
  const toStatus = statuses.find((s) => s.id === row.toStatusId);

  return (
    <tr>
      <td>
        {isReviewPlaybook(row) ? (
          <>
            {fromStatus ? <StatusPill status={fromStatus} /> : <strong>{row.fromStatusName}</strong>}{' '}
            <Text tone="muted" as="span">review playbook</Text>
          </>
        ) : (
          <>
            {fromStatus ? <StatusPill status={fromStatus} /> : <strong>{row.fromStatusName}</strong>}
            <span className="hatch-playbook-arrow">→</span>
            {toStatus ? <StatusPill status={toStatus} /> : <strong>{row.toStatusName}</strong>}
          </>
        )}
        {isUncheckedEdit(row) && <Badge tone="danger">a check with no denied tools can edit</Badge>}
        {row.unreachableReason && <Badge tone="danger">{row.unreachableReason}</Badge>}
        {limitNotes(row).map((n) => (
          <Badge key={n} tone="muted">{n}</Badge>
        ))}
      </td>
      {label !== undefined && (
        <td>
          <Text tone="muted" as="span">{label}</Text>
        </td>
      )}
      <td>
        {row.types.length > 0 ? (
          row.types.map((t) => <TypeBadge key={t} type={t} />)
        ) : (
          <Text tone="muted" as="span">any</Text>
        )}
      </td>
      <td>{row.model}</td>
      <td>{row.effort}</td>
      {label !== undefined && <td />}
    </tr>
  );
}
