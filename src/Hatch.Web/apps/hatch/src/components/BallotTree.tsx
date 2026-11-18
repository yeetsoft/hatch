import { Link } from 'react-router-dom';
import { Button, ProjectMark } from '@hatch/ui';
import { QuestionOptions } from './OpenQuestions';
import { TypeBadge } from './TypeBadge';
import { questionEmptyWords, waitedWords } from '../lib/attention';
import { appHref } from '../lib/basename';
import { projectLogoUrl } from '../lib/projectLogo';
import { scopedPath } from '../lib/scope';
import { useScope } from '../lib/useScope';
import { useStandalone } from '../lib/viewport';
import type { BallotNode, Project, Question } from '../types';

/**
 * The ballot's drawing, apart from the page that fetches it, so it can be read
 * as static markup with no router-less DOM to run it in.
 *
 * Every issue on the ballot is a container: what it is, then the questions
 * waiting on it, then the issues below it drawn the same way. The indent comes
 * from the recursion - each level wraps its children in one nested box with one
 * step of padding, as the Plan page does, so "one step per level" is a fact
 * about the markup rather than a depth anybody counts.
 */
export function BallotTree({
  roots,
  projects,
  now,
  onPreview,
}: {
  roots: BallotNode[];
  /** Every project, for each node's mark; a key naming none is drawn as letters. */
  projects: Project[];
  now: Date;
  /** Preview was pressed on an issue: the page opens the peek over the ballot. */
  onPreview: (node: BallotNode) => void;
}) {
  if (roots.length === 0) return <p className="hatch-attention-empty">{questionEmptyWords()}</p>;

  return (
    <div className="hatch-ballot">
      {roots.map((root) => (
        <BallotBranch key={root.key} node={root} projects={projects} now={now} onPreview={onPreview} top />
      ))}
    </div>
  );
}

function BallotBranch({
  node,
  projects,
  now,
  onPreview,
  top = false,
}: {
  node: BallotNode;
  projects: Project[];
  now: Date;
  onPreview: (node: BallotNode) => void;
  top?: boolean;
}) {
  const project = projects.find((p) => p.key === node.projectKey) ?? null;
  const standalone = useStandalone();
  const { keys } = useScope();

  return (
    <section className={`hatch-ballot-node${top ? ' top' : ''}`}>
      <header className="hatch-ballot-head">
        <ProjectMark
          size="md"
          letters={node.projectKey}
          color={project?.color ?? null}
          icon={project?.icon ?? null}
          logoUrl={project ? projectLogoUrl(project) : null}
          title={project?.name ?? node.projectKey}
        />
        <TypeBadge type={node.type} />
        <Link to={`/issues/${node.key}`} className="hatch-ballot-key">
          {node.key}
        </Link>
        <span className="hatch-ballot-title">{node.title}</span>
        <span className="hatch-ballot-actions">
          <Button size="sm" onClick={() => onPreview(node)}>
            Preview
          </Button>
          {/* An anchor, not a <Link>, and not in the installed app: the same
              two reasons as the peek's own "New tab" (components/IssuePeek.tsx). */}
          {!standalone && (
            <a
              href={appHref(scopedPath(`/issues/${node.key}`, keys))}
              target="_blank"
              rel="noreferrer noopener"
            >
              Open in new tab ↗
            </a>
          )}
        </span>
      </header>

      {/* Text, never markdown: it is the description's first lines as written,
          cut mid-sentence, and a half-open fence or list would draw wrong. */}
      {node.excerpt && <p className="hatch-ballot-excerpt">{node.excerpt}</p>}

      {node.questions.length > 0 && (
        <ul className="hatch-attention-rows">
          {node.questions.map((q) => (
            <li key={q.id}>
              <BallotQuestion question={q} now={now} />
            </li>
          ))}
        </ul>
      )}

      {node.children.length > 0 && (
        <div className="hatch-ballot-nested">
          {node.children.map((child) => (
            <BallotBranch key={child.key} node={child} projects={projects} now={now} onPreview={onPreview} />
          ))}
        </div>
      )}
    </section>
  );
}

/** A question in the attention panel's register, in full rather than clamped:
    this page exists to read it beside the brief. The row opens its issue; the
    issue's Preview answers it in place. */
function BallotQuestion({ question, now }: { question: Question; now: Date }) {
  return (
    <Link className="hatch-attention-row hatch-ballot-question" to={`/issues/${question.issueKey}`}>
      <span className="hatch-attention-row-head">
        <span className="hatch-ballot-asker">{question.askedBy}</span>
        <span className="hatch-attention-waited">{waitedWords(question.askedAt, now)}</span>
      </span>
      <span className="hatch-ballot-question-body">{question.body}</span>
      {question.options && <QuestionOptions options={question.options} />}
    </Link>
  );
}
