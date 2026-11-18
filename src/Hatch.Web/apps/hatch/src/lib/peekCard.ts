import type { Issue, IssueCard } from '../types';

/**
 * The card the peek draws, made from an issue read by key rather than from a
 * board.
 *
 * The Ballot has no board, so no card to hand the peek: it reads the issue and
 * lets the peek take what it needs. `Issue` carries every field an `IssueCard`
 * does except the two that only the board computes. `openQuestions` comes from
 * the caller, who counted what it is drawing. `projectPriority` is never read
 * by the peek, so it stands at its neutral level rather than costing a read of
 * the project.
 *
 * The description is left off: the peek fetches its own, and a card is not the
 * place it lives.
 */
export function cardFromIssue(issue: Issue, openQuestions: number): IssueCard {
  const { description: _description, ...rest } = issue;
  return { ...rest, openQuestions, projectPriority: 'normal' };
}
