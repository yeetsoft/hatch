import { describe, expect, it, vi } from 'vitest';
import { getBoard, getQueue } from '../api/client';
import { getFlowBoard, readFlowBoard } from './flowViz';
import type { Board, QueueEntry } from '../types';

vi.mock('../api/client', () => ({ getBoard: vi.fn(), getQueue: vi.fn() }));

const board: Board = { statuses: [], issues: [] };
const queue: QueueEntry[] = [];

describe('readFlowBoard', () => {
  it('lands both reads in one answer', async () => {
    const result = await readFlowBoard(
      () => Promise.resolve(board),
      () => Promise.resolve(queue),
    );

    expect(result.issues).toBe(board.issues);
    expect(result.queue).toBe(queue);
    expect(result.queueError).toBeNull();
  });

  it('still lands the board, with queue null, when the queue read fails', async () => {
    const result = await readFlowBoard(
      () => Promise.resolve(board),
      () => Promise.reject(new Error('the queue refused')),
    );

    expect(result.issues).toBe(board.issues);
    expect(result.queue).toBeNull();
    expect(result.queueError).toBe('the queue refused');
  });

  it('still throws when the board read fails', async () => {
    await expect(
      readFlowBoard(
        () => Promise.reject(new Error('the board refused')),
        () => Promise.resolve(queue),
      ),
    ).rejects.toThrow('the board refused');
  });
});

describe('getFlowBoard', () => {
  it('reads the queue with no runner, with takers', async () => {
    vi.mocked(getBoard).mockResolvedValueOnce(board);
    vi.mocked(getQueue).mockResolvedValueOnce(queue);

    await getFlowBoard();

    expect(getQueue).toHaveBeenCalledWith(null, true);
  });
});
