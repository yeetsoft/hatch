/* What the Budget card on an ambition says, apart from the component - the
   same split lib/spend.ts makes, and for the same reason: this app has no DOM
   test setup, so what can be wrong lives in a function with a test beside it.

   The card does no sums of its own. Every dollar figure arrives from
   GET /api/issues/{key}/budget; what is here is the percentage, the bar's
   width, the sentences, and the write. */

import { setIssueBudget } from '../api/client';
import type { IssueBudget } from '../types';
import { message } from './errors';
import { moneyPhrase } from './workLog';

/** What the amount field shows: the budget itself, blank for none. */
export function budgetDraft(budgetUsd: number | null): string {
  return budgetUsd === null ? '' : String(budgetUsd);
}

/** The field, trimmed for the wire as `amount` - blank clears the budget.
    Whether the rest is a number is the server's to say, in its own words. */
export function budgetRequest(draft: string): string {
  return draft.trim();
}

/** How much of the budget is spent, rounded to a whole percent, or null while
    there is no budget to be a share of. Not clamped: an overspent ambition
    reads over 100 rather than hiding at a false ceiling. */
export function percentSpent(budget: IssueBudget): number | null {
  if (budget.budgetUsd === null || budget.budgetUsd <= 0) return null;

  return Math.round((budget.spentUsd / budget.budgetUsd) * 100);
}

/** The bar's width as a percent, 0 to 100 - for the drawing alone, since a bar
    cannot fill past its own track. */
export function barFraction(budget: IssueBudget): number {
  const percent = percentSpent(budget);
  if (percent === null) return 0;

  return Math.min(100, Math.max(0, percent));
}

/** The one budget line on an Ambitions row: `41% spent` against a budget,
    `$12.50 spent` with none, and `Budget used up: 104% spent` once it is spent.
    Leaves out the sessions that carried no price: the ambition's own page says them. */
export function rowBudgetLine(budget: IssueBudget): string {
  const percent = percentSpent(budget);

  if (budget.spent) return percent === null ? 'Budget used up' : `Budget used up: ${percent}% spent`;
  if (percent === null) return `${moneyPhrase(budget.spentUsd)} spent`;

  return `${percent}% spent`;
}

/** The card's headline: `41% spent, for 1 of 6 capabilities`. */
export function holdsLine(percent: number, holds: number, total: number): string {
  return `${percent}% spent, for ${holds} of ${total} capabilities`;
}

/** Why a dollar figure reads as a floor, or null when every session was priced. */
export function unpricedLine(n: number): string | null {
  if (n <= 0) return null;

  return `${n} ${n === 1 ? 'session' : 'sessions'} carried no price, so this reads as at least that much.`;
}

/** Writes the budget. Answers null when it went through, or the refusal in the
    server's own words - never throws. */
export async function writeBudget(key: string, draft: string): Promise<string | null> {
  try {
    await setIssueBudget(key, budgetRequest(draft));
    return null;
  } catch (err) {
    return message(err);
  }
}
