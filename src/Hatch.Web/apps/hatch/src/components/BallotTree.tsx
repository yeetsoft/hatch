import { Link } from 'react-router-dom';
import { ProjectMark } from '@hatch/ui';
import { Asked } from './OpenQuestions';
import { TypeBadge } from './TypeBadge';
import { questionEmptyWords } from '../lib/attention';
import { projectLogoUrl } from '../lib/projectLogo';
import { fromQuestion } from '../lib/questions';
import type { BallotNode, Project } from '../types';

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
  onAnswered,
}: {
  roots: BallotNode[];
  /** Every project, for each node's mark; a key naming none is drawn as letters. */
  projects: Project[];
  /** An answer landed: the page re-reads, and the question leaves. */
  onAnswered: () => void;
}) {
  if (roots.length === 0) return <p className="hatch-attention-empty">{questionEmptyWords()}</p>;

  return (
    <div className="hatch-ballot">
      {roots.map((root) => (
        <BallotBranch key={root.key} node={root} projects={projects} onAnswered={onAnswered} top />
      ))}
    </div>
  );
}

function BallotBranch({
  node,
  projects,
  onAnswered,
  top = false,
}: {
  node: BallotNode;
  projects: Project[];
  onAnswered: () => void;
  top?: boolean;
}) {
  const project = projects.find((p) => p.key === node.projectKey) ?? null;

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
      </header>

      {/* Text, never markdown: it is the description's first lines as written,
          cut mid-sentence, and a half-open fence or list would draw wrong. */}
      {node.excerpt && <p className="hatch-ballot-excerpt">{node.excerpt}</p>}

      {node.questions.length > 0 && (
        <ul className="hatch-question-list">
          {node.questions.map((q) => (
            <Asked key={q.id} issueKey={q.issueKey} question={fromQuestion(q)} onAnswered={onAnswered} />
          ))}
        </ul>
      )}

      {node.children.length > 0 && (
        <div className="hatch-ballot-nested">
          {node.children.map((child) => (
            <BallotBranch key={child.key} node={child} projects={projects} onAnswered={onAnswered} />
          ))}
        </div>
      )}
    </section>
  );
}
