import { describe, expect, it, vi } from 'vitest';
import { setIssueBudget } from '../api/client';
import type { IssueBudget } from '../types';
import { barFraction, budgetDraft, budgetRequest, holdsLine, percentSpent, unpricedLine, writeBudget } from './budget';

vi.mock('../api/client', () => ({ setIssueBudget: vi.fn() }));

const budget = (budgetUsd: number | null, spentUsd: number): IssueBudget => ({
  budgetUsd,
  spentUsd,
  unpricedSessions: 0,
  spent: budgetUsd !== null && spentUsd >= budgetUsd,
});

describe('budgetDraft', () => {
  it('is empty for no budget', () => {
    expect(budgetDraft(null)).toBe('');
  });

  it('is the amount itself', () => {
    expect(budgetDraft(250)).toBe('250');
    expect(budgetDraft(12.5)).toBe('12.5');
  });
});

describe('budgetRequest', () => {
  it('trims the draft', () => {
    expect(budgetRequest(' 250 ')).toBe('250');
  });

  it('trims whitespace alone down to blank, which clears', () => {
    expect(budgetRequest('   ')).toBe('');
  });
});

describe('percentSpent', () => {
  it('rounds to a whole percent', () => {
    expect(percentSpent(budget(100, 41.4))).toBe(41);
    expect(percentSpent(budget(100, 41.5))).toBe(42);
  });

  it('is null with no budget', () => {
    expect(percentSpent(budget(null, 12))).toBeNull();
  });

  it('is 0 before anything is spent', () => {
    expect(percentSpent(budget(100, 0))).toBe(0);
  });

  it('reads over 100 once overspent', () => {
    expect(percentSpent(budget(100, 130))).toBe(130);
  });
});

describe('barFraction', () => {
  it('is the percent while under budget', () => {
    expect(barFraction(budget(100, 41))).toBe(41);
  });

  it('stops at 100 when overspent', () => {
    expect(barFraction(budget(100, 130))).toBe(100);
  });

  it('is 0 with no budget', () => {
    expect(barFraction(budget(null, 12))).toBe(0);
  });
});

describe('holdsLine', () => {
  it('is the drawing line', () => {
    expect(holdsLine(41, 1, 6)).toBe('41% spent, for 1 of 6 capabilities');
  });
});

describe('unpricedLine', () => {
  it('is null when every session was priced', () => {
    expect(unpricedLine(0)).toBeNull();
  });

  it('is singular at one', () => {
    expect(unpricedLine(1)).toBe('1 session carried no price, so this reads as at least that much.');
  });

  it('is plural past one', () => {
    expect(unpricedLine(3)).toBe('3 sessions carried no price, so this reads as at least that much.');
  });
});

describe('writeBudget', () => {
  it('calls setIssueBudget once with the trimmed amount and answers null', async () => {
    vi.mocked(setIssueBudget).mockResolvedValueOnce({} as never);

    await expect(writeBudget('HA-5', ' 250 ')).resolves.toBeNull();

    expect(setIssueBudget).toHaveBeenCalledTimes(1);
    expect(setIssueBudget).toHaveBeenCalledWith('HA-5', '250');
  });

  it('sends a blank amount to clear it', async () => {
    vi.mocked(setIssueBudget).mockResolvedValueOnce({} as never);

    await writeBudget('HA-5', '');

    expect(setIssueBudget).toHaveBeenLastCalledWith('HA-5', '');
  });

  it('answers the server refusal sentence instead of throwing', async () => {
    vi.mocked(setIssueBudget).mockRejectedValueOnce(new Error('a budget is a positive number of dollars, to the cent - not "abc"'));

    await expect(writeBudget('HA-5', 'abc')).resolves.toBe('a budget is a positive number of dollars, to the cent - not "abc"');
  });
});
