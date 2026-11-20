/* The ballot drawn one card at a time: which question is on the card, and the
   sentences around it. Pure, so the order and the words can be read without a
   page to render. */

import type { BallotNode, BallotQuestion, Comment, QuestionOption } from '../types';

/** The root holding `issueKey` at any depth, or null where no root does. */
export function rootOf(roots: BallotNode[], issueKey: string): BallotNode | null {
  const holds = (node: BallotNode): boolean => node.key === issueKey || node.children.some(holds);
  return roots.find(holds) ?? null;
}

/**
 * The questions in the order the cards come: those not skipped, in the order
 * the server ranked them, then those skipped, in the order they were skipped.
 * A skipped id the server no longer lists - answered elsewhere - is dropped.
 */
export function cardQueue(questions: BallotQuestion[], skipped: number[]): BallotQuestion[] {
  const byId = new Map(questions.map((q) => [q.question.id, q]));
  const later = skipped.flatMap((id) => byId.get(id) ?? []);
  const skippedIds = new Set(skipped);
  return [...questions.filter((q) => !skippedIds.has(q.question.id)), ...later];
}

/** The options with the recommended one first and the rest as they were. */
export const optionsRecommendedFirst = (options: QuestionOption[]): QuestionOption[] => [
  ...options.filter((o) => o.recommended),
  ...options.filter((o) => !o.recommended),
];

/** What answering the question lets go of, as the pill says it. */
export const holdsWords = (holds: number): string =>
  holds === 0 ? 'Holds nothing' : `Holds ${holds} ${holds === 1 ? 'issue' : 'issues'}`;

/** The card's footer: what is behind this question. A look-back question is one
    already decided without the person, so it is counted apart. */
export function thenWords(open: number, lookBack: number): string {
  const clauses = [
    open > 0 ? `${open} more to answer` : null,
    lookBack > 0 ? `${lookBack} decided without you to look over` : null,
  ].filter((c): c is string => c !== null);
  return clauses.length === 0 ? 'Nothing else is waiting' : `Then ${clauses.join(', and ')}`;
}

/** The recommended option's label - what a look-back question's work went ahead
    on - or null where the question recommended none. */
export const keepOption = (options: QuestionOption[] | null): string | null =>
  options?.find((o) => o.recommended)?.label ?? null;

/** The first answer a key wrote, split at its first non-blank line - who
    wrote it, that line, and everything after, trimmed - or null where no key
    has answered. What a look-back draws instead of "Decided without you". */
export function programAnswer(answers: Comment[]): { by: string; first: string; rest: string } | null {
  const answer = answers.find((a) => a.authorKind === 'key');
  if (!answer) return null;

  const lines = answer.body.replace(/\r\n/g, '\n').split('\n');
  const at = lines.findIndex((l) => l.trim().length > 0);
  if (at === -1) return { by: answer.author, first: '', rest: '' };

  return { by: answer.author, first: lines[at].trim(), rest: lines.slice(at + 1).join('\n').trim() };
}

/** What has happened to the issue since the decision, as one sentence: the
    column it stands in, when it closed, and how its work reached the trunk.
    Each clause is left out where the route carries nothing for it. */
export function outcomeWords(item: BallotQuestion): string {
  const clauses = [
    item.issueStatus ? `it stands in ${item.issueStatus}` : null,
    item.closedAt ? `closed ${new Date(item.closedAt).toLocaleDateString()}` : null,
    item.merged === true
      ? 'its pull request merged'
      : item.landings && item.landings.length > 0
        ? 'its work landed on the trunk'
        : null,
  ].filter((c): c is string => c !== null);
  if (clauses.length === 0) return '';
  const sentence = clauses.join(', ');
  return `${sentence[0].toUpperCase()}${sentence.slice(1)}.`;
}
