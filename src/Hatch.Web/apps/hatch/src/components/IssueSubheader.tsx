import { Link } from 'react-router-dom';
import { ProjectMark } from '@hatch/ui';
import { BuildCheckChips } from './BuildCheckChips';
import { MergeConflictChips } from './MergeConflictChips';
import { MomentChip } from './MomentChip';
import { PullRequestLink } from './PullRequestLink';
import { TypeBadge } from './TypeBadge';
import { projectLogoUrl } from '../lib/projectLogo';
import type { Issue, Project } from '../types';

/**
 * The issue page's header, under the title: what this is - project, key, type,
 * parent - drawn larger and first, and everything else about it - ready, due,
 * pull request, the checks, who filed it - smaller and after.
 *
 * Rendered inside PageHeader's `description`, which is inside a `<p>`
 * (PageHeader.tsx) - so this and everything in it stays inline-category. A
 * `<div>` here would close that paragraph early.
 */
export function IssueSubheader({
  issue,
  project,
  stopped,
}: {
  issue: Issue;
  /** The issue's own project, or null while it is still loading. */
  project: Project | null;
  /** Shipped or shelved, so the due chip stops warning - see isSettled. */
  stopped: boolean;
}) {
  return (
    <span className="hatch-issue-subheader">
      <span className="hatch-issue-identity">
        <ProjectMark
          size="md"
          letters={project?.key ?? issue.projectKey}
          color={project?.color ?? null}
          icon={project?.icon ?? null}
          logoUrl={project ? projectLogoUrl(project) : null}
          title={project?.name ?? issue.projectKey}
        />
        <span className="hatch-issue-key">{issue.key}</span>
        <TypeBadge type={issue.type} />
        {issue.parentKey && <Link to={`/issues/${issue.parentKey}`}>↳ {issue.parentKey}</Link>}
      </span>
      <span className="hatch-issue-meta">
        <MomentChip kind="ready" value={issue.readyAt} expandable />
        <MomentChip kind="due" value={issue.dueAt} muted={stopped} expandable />
        <PullRequestLink url={issue.pullRequestUrl} />
        <MergeConflictChips checks={issue.mergeChecks} />
        <BuildCheckChips checks={issue.buildChecks} />
        <span className="text-muted">
          filed by {issue.createdBy} on {new Date(issue.createdAt).toLocaleDateString()}
        </span>
      </span>
    </span>
  );
}
