import { message } from './errors';
import type { RunnerPatchRequest } from '../types';

/**
 * One of the runner detail's saves: PATCH the runner, then ask again.
 *
 * Returns the sentence the card should show - the server's refusal - or null
 * where the save went through. The reload runs either way, as `act()` did on
 * the old runners page: a refused press must not leave the card drawing what
 * was asked for rather than what is true.
 *
 * `patch` and `reload` are passed in so what a press does is testable without
 * a click: the cards hand over `patchRunner` and the layout's `reload`.
 */
export async function saveRunner(
  name: string,
  request: RunnerPatchRequest,
  { patch, reload }: { patch: (name: string, request: RunnerPatchRequest) => Promise<unknown>; reload: () => Promise<void> },
): Promise<string | null> {
  try {
    await patch(name, request);
    return null;
  } catch (err) {
    return message(err);
  } finally {
    await reload();
  }
}
