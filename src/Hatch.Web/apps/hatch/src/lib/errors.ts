/** The sentence to put on screen for whatever a rejected promise carried. */
export function message(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/**
 * What a refused request's body says out loud, or `fallback` when it says
 * nothing worth reading.
 *
 * Hatch's refusals are written to be read: "AER still has 3 issues in it",
 * "a story hangs under an epic, not a task". Reporting "DELETE /api/hatch/…
 * failed: 409" instead throws away the only sentence that says what to do
 * about it.
 *
 * A bare string body arrives JSON-quoted - most of Hatch's refusals. A
 * structured body carries its sentence in `error`, following `AuthErrorDto`'s
 * `{ error }` shape (`src/Hatch.Api/Models/Auth/Dtos.cs`) - the WIP `409`'s
 * `{ error, load, limit }` is one of these. Falls back on anything else -
 * empty, HTML, over 300 characters, a ProblemDetails blob with no `error`,
 * or a body that is not JSON at all - because one of those under a text
 * input is worse than nothing.
 */
export function refusalSentence(text: string, fallback: string): string {
  const body = text.trim();
  if (body === '' || body.length > 300) return fallback;

  try {
    const parsed: unknown = JSON.parse(body);
    if (typeof parsed === 'string') return parsed !== '' && !parsed.startsWith('<') ? parsed : fallback;
    if (
      parsed !== null &&
      typeof parsed === 'object' &&
      'error' in parsed &&
      typeof (parsed as { error: unknown }).error === 'string'
    ) {
      return (parsed as { error: string }).error;
    }
    return fallback;
  } catch {
    return fallback;
  }
}

/**
 * A request the server answered and refused. The message is the server's own
 * sentence; the status is for the caller that has to tell a refusal it can
 * explain (a 409: the card is not where you left it) from a failure it can
 * only report (the server unreachable, a 500).
 */
export class HttpError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(message: string, status: number, body: unknown = undefined) {
    super(message);
    this.name = 'HttpError';
    this.status = status;
    this.body = body;
  }
}
