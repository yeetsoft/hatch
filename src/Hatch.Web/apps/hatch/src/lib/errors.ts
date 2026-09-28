/** The sentence to put on screen for whatever a rejected promise carried. */
export function message(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/**
 * A request the server answered and refused. The message is the server's own
 * sentence; the status is for the caller that has to tell a refusal it can
 * explain (a 409: the card is not where you left it) from a failure it can
 * only report (the server unreachable, a 500).
 */
export class HttpError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'HttpError';
    this.status = status;
  }
}
