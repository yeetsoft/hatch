import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { promotableAnswer, PromotePress } from './PromotePress';
import type { Comment } from '../types';

vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));

const comment = (over: Partial<Comment>): Comment => ({
  id: 1,
  author: 'a-person',
  body: 'Say it plainly.',
  kind: 'answer',
  answersId: 2,
  options: null,
  createdAt: '2026-01-10T09:00:00Z',
  deliveredAt: null,
  deliveredTo: null,
  level: null,
  answeredAfter: false,
  authorKind: null,
  ...over,
});

const render = (ambitionKey: string | null | undefined) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <PromotePress issueKey="HA-12" questionId={2} ambitionKey={ambitionKey} answer="Say it plainly." />
    </MemoryRouter>,
  );

describe('PromotePress', () => {
  it('draws the press when the question has an ambition above it', () => {
    expect(render('HA-9')).toContain('Make this a principle');
  });

  it('draws nothing where the question has no ambition above it', () => {
    expect(render(null)).toBe('');
    expect(render(undefined)).toBe('');
  });
});

describe('promotableAnswer', () => {
  it('picks the newest answer', () => {
    const older = comment({ id: 1, createdAt: '2026-01-10T09:00:00Z' });
    const newer = comment({ id: 2, createdAt: '2026-01-11T09:00:00Z' });
    expect(promotableAnswer([older, newer])).toBe(newer);
  });

  it('breaks a tie on id', () => {
    const lower = comment({ id: 1, createdAt: '2026-01-10T09:00:00Z' });
    const higher = comment({ id: 2, createdAt: '2026-01-10T09:00:00Z' });
    expect(promotableAnswer([lower, higher])).toBe(higher);
  });

  it('returns null for no answers', () => {
    expect(promotableAnswer([])).toBeNull();
  });
});
