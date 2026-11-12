import { message } from './errors';
import { refusedModelsRefusal } from './runnerModels';
import { sameTags } from './runnerTags';
import type { RunnerSettingsRequest } from './runners';
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

/**
 * The settings card's save: the same PATCH as `saveRunner`, then the refused
 * models on their own route - only where the list changed, so a save of tags
 * alone sends no `PUT`.
 *
 * Returns the sentence the card should show, or null. A bad entry in a
 * changed list is refused with the API's own sentence before either request
 * goes, and reloads nothing since nothing was sent; a list left as it was is
 * not judged, so a held entry the web would not have accepted never blocks a
 * save of tags. A refused PATCH skips the `PUT`, so the one save is
 * not half-applied by a press that was already told no. The reload runs once,
 * after whichever requests went.
 */
export async function saveRunnerSettings(
  name: string,
  request: RunnerSettingsRequest,
  held: string[],
  agent: string | null,
  {
    patch,
    putRefused,
    reload,
  }: {
    patch: (name: string, request: RunnerPatchRequest) => Promise<unknown>;
    putRefused: (name: string, models: string[]) => Promise<unknown>;
    reload: () => Promise<void>;
  },
): Promise<string | null> {
  const changed = !sameTags(request.refusedModels, held);
  const refusal = changed ? refusedModelsRefusal(request.refusedModels, agent) : null;
  if (refusal) return refusal;

  try {
    await patch(name, request.patch);
    if (changed) await putRefused(name, request.refusedModels);
    return null;
  } catch (err) {
    return message(err);
  } finally {
    await reload();
  }
}
