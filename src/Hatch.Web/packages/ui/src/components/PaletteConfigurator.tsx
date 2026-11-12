import {
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore,
  type KeyboardEvent,
} from 'react';
import { createPortal } from 'react-dom';
import { derivePalette } from '../theme/palette/derive';
import { IDENTITY } from '../theme/palette/params';
import { measurePairs } from '../theme/palette/pairs';
import { decodeSeed, encodeSeed } from '../theme/palette/seed';
import { BLOCKS } from '../theme/themeStore';
import { createTuneSession } from '../theme/tuneSession';
import { useTheme } from '../theme/useTheme';
import { Modal } from './Modal';
import { PaletteBoard } from './PaletteBoard';
import { ParameterControls } from './ParameterControls';
import { PickerIcon } from './PickerIcon';
import { COPY_SHAPES } from './themePickerIcons';
import { useCopyAnnounce } from './useCopyAnnounce';
import './PaletteConfigurator.css';

export interface PaletteConfiguratorProps {
  open: boolean;
  /** Called after the session has settled - reverted by every way out but Apply, applied by Apply. */
  onClose: () => void;
}

/** The seed the house palette stands for: the candidate a session starts from when none is applied. */
const HOUSE_SEED = encodeSeed(IDENTITY);

/**
 * The palette configurator: a console-toned dialog that previews a candidate on the page behind it as the controls
 * move, and either puts it back or applies it.
 *
 * It is rendered through a portal into <body>. <ThemeSwitch> lives in the gear menu's panel, which translates and
 * clips, and a `position: fixed` overlay inside a transformed ancestor is positioned against that ancestor. Because
 * the portal keeps the React tree, the dialog's events still bubble to the menu; the menu only ever closes on them.
 *
 * `Session` is what is mounted while open, so mounting is the snapshot: every opening starts from the palette on
 * screen and nothing from the last one survives. Opening previews nothing.
 *
 * The first control after the head's close button is the dial, then each slider, the seed field, copy, Roll, Revert
 * and Apply - the close control is Modal's and comes first in every Modal, as does the panel taking focus on open.
 */
export function PaletteConfigurator({ open, onClose }: PaletteConfiguratorProps) {
  if (!open || typeof document === 'undefined') return null;
  return createPortal(<Session onClose={onClose} />, document.body);
}

function Session({ onClose }: { onClose: () => void }) {
  const { palette, resolved, previewPalette, setPalette } = useTheme();

  /* The ports read the theme through refs, so a render of the provider does not rebuild the session and drop the
     candidate with it. */
  const previewRef = useRef(previewPalette);
  const applyRef = useRef(setPalette);
  previewRef.current = previewPalette;
  applyRef.current = setPalette;

  const [session] = useState(() =>
    createTuneSession(palette, {
      preview: (seed) => previewRef.current(seed),
      apply: (seed) => applyRef.current(seed),
      schedule: (run) => {
        const frame = requestAnimationFrame(run);
        return () => cancelAnimationFrame(frame);
      },
    }),
  );
  const state = useSyncExternalStore(session.subscribe, session.getState, session.getState);

  /* Unmounting any other way than Close or Apply (the page navigating under it) must not leave a candidate on
     screen. */
  useEffect(() => () => session.dispose(), [session]);

  /* What the board shows is what the page was last given: the previewed seed, one derivation per frame, and the
     house palette's own vector until something is previewed. */
  const shown = useMemo(() => {
    const decoded = decodeSeed(palette ?? HOUSE_SEED);
    const derived = derivePalette(decoded.ok ? decoded.params : IDENTITY, BLOCKS[resolved]);
    return { palette: derived, pairs: measurePairs(derived) };
  }, [palette, resolved]);

  const messageId = useId();
  const [draft, setDraft] = useState(state.seed);
  const { message: copied, copy } = useCopyAnnounce();

  /* A move, a roll or a loaded seed changes the seed under the field; what was typed gives way to it. */
  useLayoutEffect(() => {
    setDraft(state.seed);
  }, [state.seed]);

  const revert = () => {
    session.revert();
    setDraft(session.getState().seed);
  };

  const close = () => {
    session.revert();
    onClose();
  };

  const commit = () => {
    const text = draft.trim();
    if (text === '') return setDraft(state.seed);
    if (text === state.seed) return;
    session.load(text);
    if (session.getState().refusal === null) setDraft(session.getState().seed);
  };

  const onSeedKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      commit();
    } else if (event.key === 'Escape' && (draft !== state.seed || state.refusal !== null)) {
      /* Take back what was typed, and keep the Escape: the dialog would close on it otherwise. */
      event.stopPropagation();
      setDraft(state.seed);
      session.clearRefusal();
    }
  };

  const footer = (
    <div className="hatch-configurator__foot">
      <div className="hatch-configurator__seed">
        <input
          type="text"
          className="hatch-configurator__seed-input"
          aria-label="Theme seed"
          aria-invalid={state.refusal !== null || undefined}
          aria-describedby={state.refusal !== null ? messageId : undefined}
          spellCheck={false}
          autoComplete="off"
          autoCapitalize="off"
          value={draft}
          onChange={(event) => {
            setDraft(event.target.value);
            session.clearRefusal();
          }}
          onKeyDown={onSeedKeyDown}
          onBlur={commit}
        />
        <button
          type="button"
          className="hatch-configurator__icon"
          aria-label="Copy seed"
          title="Copy seed"
          onClick={() => copy(state.seed, 'Seed copied', 'Could not copy - select the seed and copy it by hand')}
        >
          <PickerIcon shapes={COPY_SHAPES} />
        </button>
      </div>
      <div className="hatch-configurator__actions">
        <button type="button" className="hatch-configurator__action" onClick={() => session.roll()}>
          Roll
        </button>
        <button type="button" className="hatch-configurator__action" onClick={revert}>
          Revert
        </button>
        <button
          type="button"
          className="hatch-configurator__action hatch-configurator__action--primary"
          onClick={() => {
            session.apply();
            onClose();
          }}
        >
          Apply
        </button>
      </div>
      {state.refusal !== null ? (
        <p id={messageId} className="hatch-configurator__message" role="alert">
          {state.refusal}
        </p>
      ) : null}
      <span className="hatch-configurator__message hatch-configurator__status" role="status">
        {copied}
      </span>
    </div>
  );

  return (
    <Modal open onClose={close} title="Tune the theme" tone="console" footer={footer}>
      <div className="hatch-configurator">
        <p className="hatch-configurator__block">Showing the {resolved} block</p>
        <PaletteBoard palette={shown.palette} pairs={shown.pairs} />
        <ParameterControls value={state.params} onChange={session.move} palette={shown.palette} />
      </div>
    </Modal>
  );
}
