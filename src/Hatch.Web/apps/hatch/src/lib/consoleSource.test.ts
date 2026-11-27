import { describe, expect, it } from 'vitest';
import type { IssueCard, Status } from '../types';
import { consoleSource } from './consoleSource';

const card = (key: string) => ({ key }) as IssueCard;
const status = (id: number) => ({ id }) as Status;

describe('consoleSource', () => {
  it("is the host's cards and columns, whatever the page's moves hold", () => {
    const host = { cards: [card('HA-1'), card('HA-2')], statuses: [status(1)] };
    const moves = { card: card('HA-9'), statuses: [status(7)] };
    expect(consoleSource(host, moves)).toEqual({ cards: host.cards, statuses: host.statuses });
  });

  it("is the page's own card and columns when there is no host", () => {
    const moves = { card: card('HA-9'), statuses: [status(7), status(8)] };
    expect(consoleSource(null, moves)).toEqual({ cards: [moves.card], statuses: moves.statuses });
  });

  it('is nothing with no host and nothing offered', () => {
    expect(consoleSource(null, null)).toEqual({ cards: [], statuses: [] });
    expect(consoleSource(null, { card: null })).toEqual({ cards: [], statuses: [] });
  });
});
