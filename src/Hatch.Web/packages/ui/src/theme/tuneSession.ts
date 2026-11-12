/* The configurator's state machine, with no React and no DOM in it so a node test can drive it with fake ports.
   ---------------------------------------------------------------------------
   A session is one opening of the modal. It is made from the palette that was on screen when the modal opened (the
   snapshot: a stored seed, or null for the house palette), holds the candidate vector, and talks to the page only
   through three ports, so what it does to storage and to the document is decided by the caller and not by this file.

   - `preview` shows a seed on the page without writing it anywhere.
   - `apply` commits a seed, the one write a session ever makes.
   - `schedule` runs a callback on the next frame and returns its cancel, so a drag's many moves cost one preview.

   The candidate is always held as what a seed can hold: a drawn vector, a loaded one and a rolled one all pass
   through the codec, so what is on screen is what the seed field says and what Apply writes. */
import { cryptoRandom } from './randomPalette';
import { decodeSeed, encodeSeed } from './palette/seed';
import { IDENTITY, rollParams, type PaletteParams } from './palette/params';

export interface TunePorts {
  /** Shows `seed` on the page, or the house palette for `null`. Writes nothing. */
  preview: (seed: string | null) => void;
  /** Commits `seed`. Called at most once per apply, and never by a preview. */
  apply: (seed: string) => void;
  /** Runs `run` on the next frame; the return is its cancel. */
  schedule: (run: () => void) => () => void;
}

export interface TuneState {
  /** The candidate vector the controls draw. */
  params: PaletteParams;
  /** What the seed field shows: always `encodeSeed(params)`. */
  seed: string;
  /** The words a refused seed was refused in, or null. */
  refusal: string | null;
}

export interface TuneSession {
  getState: () => TuneState;
  subscribe: (listener: () => void) => () => void;
  /** A control moved. State is set now; one preview follows on the next frame. */
  move: (params: PaletteParams) => void;
  /** Draws a vector from the roll windows, as `move` would take it. */
  roll: (random?: () => number) => void;
  /** A seed committed in the field. Loaded and previewed at once, or refused in words and nothing else changes. */
  load: (text: string) => void;
  /** Takes the refusal down, for the field it was shown beside being edited again. */
  clearRefusal: () => void;
  /** Back to the snapshot: the controls reset and the page is shown the snapshot, at once. */
  revert: () => void;
  /** Commits the candidate through `ports.apply`. */
  apply: () => void;
  /** Cancels a pending frame and, if the page is still showing a candidate nobody settled, shows the snapshot
      again. Safe to call twice, and the session stays usable (a strict-mode remount runs it once on a fresh one). */
  dispose: () => void;
}

const stateOf = (params: PaletteParams, refusal: string | null = null): TuneState => ({
  params,
  seed: encodeSeed(params),
  refusal,
});

/** The vector a seed holds: what a stored seed derives, which is what a page gets. */
function paramsOfSeed(seed: string | null): PaletteParams {
  if (seed === null) return { ...IDENTITY };
  const decoded = decodeSeed(seed);
  return decoded.ok ? decoded.params : { ...IDENTITY };
}

export function createTuneSession(snapshot: string | null, ports: TunePorts): TuneSession {
  const start = paramsOfSeed(snapshot);
  let state = stateOf(start);
  const listeners = new Set<() => void>();
  let cancelFrame: (() => void) | null = null;
  /** The page is showing something this session put there and has not settled by Revert or Apply. */
  let showing = false;

  const set = (next: TuneState) => {
    state = next;
    for (const listener of [...listeners]) listener();
  };

  const dropFrame = () => {
    cancelFrame?.();
    cancelFrame = null;
  };

  /* The frame reads the state it runs in, not the one that scheduled it, so a drag's last move is what is shown.
     A move while one is pending schedules nothing. */
  const previewSoon = () => {
    if (cancelFrame) return;
    cancelFrame = ports.schedule(() => {
      cancelFrame = null;
      showing = true;
      ports.preview(state.seed);
    });
  };

  const previewNow = () => {
    dropFrame();
    showing = true;
    ports.preview(state.seed);
  };

  return {
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    move(params) {
      set(stateOf(params));
      previewSoon();
    },
    roll(random = cryptoRandom) {
      /* Through the codec: the screen holds what a seed holds, and a rolled vector is not on the seed's grid. */
      const rolled = decodeSeed(encodeSeed(rollParams(random)));
      if (!rolled.ok) return;
      set(stateOf(rolled.params));
      previewSoon();
    },
    load(text) {
      const decoded = decodeSeed(text);
      if (!decoded.ok) {
        if (state.refusal !== decoded.message) set({ ...state, refusal: decoded.message });
        return;
      }
      set(stateOf(decoded.params));
      previewNow();
    },
    clearRefusal() {
      if (state.refusal !== null) set({ ...state, refusal: null });
    },
    revert() {
      dropFrame();
      set(stateOf({ ...start }));
      showing = false;
      ports.preview(snapshot);
    },
    apply() {
      dropFrame();
      showing = false;
      ports.apply(state.seed);
    },
    dispose() {
      dropFrame();
      if (!showing) return;
      showing = false;
      ports.preview(snapshot);
    },
  };
}
