import { Link } from 'react-router-dom';
import { Field, ProjectMark } from '@hatch/ui';
import { assigneeFact, effortFact, expressFact, modelFact, priorityFact, wipLimitFact } from '../lib/issueFacts';
import { projectLogoUrl } from '../lib/projectLogo';
import { MomentChip } from './MomentChip';
import { PullRequestLink } from './PullRequestLink';
import { TypeBadge } from './TypeBadge';
import type { Issue, Project } from '../types';

/**
 * The primary-info card's read mode - every one of its twelve fields as
 * text, a badge or a mark, and nothing in the tab order except a Parent link
 * and a PullRequestLink (see HA-334's own Done list: §6.2/§6.3's test pins
 * no `<select>`, no `<input>`, no `<button>`, and says nothing about `<a>`,
 * because a link does not write anything).
 *
 * `stopped` is handed down rather than recomputed - `IssuePage.tsx`'s own
 * `isSettled(...)` - so Due's muting can never disagree with the status bar
 * above it.
 */
export function IssueFacts({ issue, project, stopped }: { issue: Issue; project: Project | null; stopped: boolean }) {
  return (
    <div className="hatch-issue-controls">
      <Field label="Type" as="div">
        <TypeBadge type={issue.type} />
      </Field>

      <Field label="Project" as="div">
        <span className="hatch-issue-project">
          <ProjectMark
            size="sm"
            letters={project?.key ?? issue.projectKey}
            color={project?.color ?? null}
            icon={project?.icon ?? null}
            logoUrl={project ? projectLogoUrl(project) : null}
            title={project?.name ?? issue.projectKey}
          />
          {project?.name ?? issue.projectKey}
        </span>
      </Field>

      <Field label="Parent" as="div">
        {issue.parentKey ? <Link to={`/issues/${issue.parentKey}`}>{issue.parentKey}</Link> : <span className="text-muted">—</span>}
      </Field>

      <Field label="Assignee" as="div">
        {assigneeFact(issue.assignee)}
      </Field>

      <Field label="Priority" as="div">
        {priorityFact(issue.priority, issue.priorityFrom)}
      </Field>

      <Field label="Express" as="div">
        {expressFact(issue.express)}
      </Field>

      {/* Not `expandable`: that draws the chip as a <button>, which is a
          second focusable press this card is not supposed to carry in read
          mode (§6.2/§6.3) - the subheader's own use of it, above this card,
          is a different question since nothing there toggles an edit. */}
      <Field label="Ready" as="div">
        {issue.readyAt ? <MomentChip kind="ready" value={issue.readyAt} /> : <span className="text-muted">—</span>}
      </Field>

      <Field label="Due" as="div">
        {issue.dueAt ? <MomentChip kind="due" value={issue.dueAt} muted={stopped} /> : <span className="text-muted">—</span>}
      </Field>

      <Field label="Pull request" as="div">
        {issue.pullRequestUrl ? <PullRequestLink url={issue.pullRequestUrl} /> : <span className="text-muted">—</span>}
      </Field>

      <Field label="Model" as="div">
        {modelFact(issue.modelOverride)}
      </Field>

      <Field label="Effort" as="div">
        {effortFact(issue.effortOverride)}
      </Field>

      {issue.type === 'epic' && (
        <Field label="Stories at once" as="div">
          {wipLimitFact(issue.wipLimit)}
        </Field>
      )}
    </div>
  );
}
