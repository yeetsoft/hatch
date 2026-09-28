/* A box for writing markdown in: VS Code's editor where there is a pointer to
   use it, the plain <textarea> everywhere else.

   Two bodies, one component. `PlainEditor` is exactly the box this replaced -
   a <textarea> that grows with its text - and is what a touch screen gets for
   good (Monaco does not support touch browsers) and what everybody else has
   until the editor's chunk has arrived. `MonacoEditor` is the editor, in the
   house tokens. The value is the caller's state, so the text carries across
   the swap on its own; what would not is the caret, so the swap carries that.

   Loading is what keeps a page no heavier than it was. The editor is
   `lib/monaco.ts`, and this is the one place that reaches it, with a dynamic
   import that Vite cuts into its own chunk. By default it is requested on
   mount - right for a box somebody opens with a gesture (Edit, New issue). A box
   that is already on the page when it loads passes `deferred`, and the request
   waits for the first focus or pointer over its plain box, so the page pays
   nothing until somebody reaches for it. A request from `focus` lands mid-typing,
   which is what the caret carry-over is for.

   Lives here and not in @hatch/ui: the ui barrel pulls every component's CSS
   into every app, and only this app writes prose. */

import { useEffect, useImperativeHandle, useRef, useState, type Ref } from 'react';
import { useTheme } from '@hatch/ui';
import type { editor } from 'monaco-editor/editor/editor.api';
import { editorTheme, readTokens } from '../lib/editorTheme';
import { clampedHeight, parseCeiling, useAutoGrow } from '../lib/useAutoGrow';

export interface MarkdownEditorProps {
  value: string;
  onChange: (next: string) => void;
  /** The height it opens at, and the floor it never goes back under. */
  rows: number;
  /** The ceiling, as it was on the textarea: a class whose `max-height` is the
      most the box grows to. Lands on the host, so the max-height is the host's. */
  className?: string;
  /** Shown while empty. */
  placeholder?: string;
  /** What a screen reader calls it. Where the box sits in a `Field` this is the
      field's label text, and the `Field` is rendered `as="div"` - see
      `focusOnLabelClick`. */
  ariaLabel: string;
  /** The box is on the page from the start rather than opened by a gesture:
      fetch the editor when it is first focused or pointed at, not before. */
  deferred?: boolean;
}

type MonacoModule = typeof import('../lib/monaco');

// One request for the whole page: the module is evaluated once, and every
// editor after the first mounts straight into it.
let loading: Promise<MonacoModule> | null = null;
let loaded: MonacoModule | null = null;

function loadMonaco(): Promise<MonacoModule> {
  loading ??= import('../lib/monaco').then(
    (m) => (loaded = m),
    (err) => {
      // A chunk that would not arrive is asked for again next time, not cached.
      loading = null;
      throw err;
    },
  );
  return loading;
}

const isCoarse = () => typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches;

/** Where a caret was in the box that is being swapped out. */
interface Carry {
  focused: boolean;
  start: number;
  end: number;
}

/**
 * Focuses the box when its `Field`'s label is clicked. A <label> chooses its
 * control as the first labelable descendant, and Monaco has none it can be
 * relied on to choose - the answer differs by browser - so a site renders its
 * `Field` `as="div"` and this does the association. It reaches into `Field`'s
 * class names: the one coupling to @hatch/ui's markup.
 */
function focusOnLabelClick(from: HTMLElement, focus: () => void): () => void {
  const label = from.closest('.hatch-field__control')?.querySelector('.hatch-field__label');
  label?.addEventListener('click', focus);
  return () => label?.removeEventListener('click', focus);
}

export function MarkdownEditor(props: MarkdownEditorProps) {
  const [coarse] = useState(isCoarse);
  const [wanted, setWanted] = useState(!props.deferred);
  // The editor once it is there, and the caret of the box it replaced.
  const [ready, setReady] = useState<{ monaco: MonacoModule; from: Carry | null } | null>(
    !coarse && loaded ? { monaco: loaded, from: null } : null,
  );
  const plain = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (coarse || !wanted || ready) return;
    let alive = true;
    loadMonaco().then(
      (monaco) => {
        if (!alive) return;
        const el = plain.current;
        const from = el && { focused: document.activeElement === el, start: el.selectionStart, end: el.selectionEnd };
        setReady({ monaco, from });
      },
      // Not arriving leaves the plain box, which still writes the same value.
      () => {},
    );
    return () => {
      alive = false;
    };
  }, [coarse, wanted, ready]);

  if (ready) return <MonacoEditor {...props} monaco={ready.monaco} from={ready.from} />;
  return <PlainEditor {...props} ref={plain} onWant={coarse ? undefined : () => setWanted(true)} />;
}

function PlainEditor({
  value,
  onChange,
  rows,
  className,
  placeholder,
  ariaLabel,
  ref,
  onWant,
}: MarkdownEditorProps & {
  /** The textarea, for the parent to read its caret from at the swap. */
  ref: Ref<HTMLTextAreaElement>;
  /** Somebody reached for the box: the first focus or pointer over it. */
  onWant?: () => void;
}) {
  const grow = useAutoGrow(value);
  useImperativeHandle(ref, () => grow.current as HTMLTextAreaElement, [grow]);

  useEffect(() => {
    const el = grow.current;
    return el ? focusOnLabelClick(el, () => el.focus()) : undefined;
  }, [grow]);

  return (
    <textarea
      ref={grow}
      className={`hatch-description-editor${className ? ` ${className}` : ''}`}
      rows={rows}
      value={value}
      placeholder={placeholder}
      aria-label={ariaLabel}
      onChange={(e) => onChange(e.target.value)}
      onFocus={onWant}
      onPointerEnter={onWant}
    />
  );
}

/** Monaco reads text with `\n`, and so does this: a stored description with
    `\r\n` must not differ from the draft made of it before a key is pressed. */
const lf = (text: string) => text.replace(/\r\n?/g, '\n');

// Named once per appearance and defined once. `setTheme` returns early when the
// theme object is the one it already has, so recolouring is switching between
// two names, not redefining one.
const definedThemes = new Set<string>();

function applyTheme(monaco: MonacoModule, resolved: 'light' | 'dark') {
  const name = `hatch-${resolved}`;
  if (!definedThemes.has(name)) {
    const tokens = readTokens(getComputedStyle(document.documentElement));
    monaco.editor.defineTheme(name, editorTheme(tokens, resolved === 'dark'));
    definedThemes.add(name);
  }
  // Global to the page: every open editor follows.
  monaco.editor.setTheme(name);
}

const px = (value: string) => {
  const n = parseFloat(value);
  return Number.isFinite(n) ? n : 0;
};

function MonacoEditor({
  monaco,
  from,
  value,
  onChange,
  rows,
  className,
  placeholder,
  ariaLabel,
}: MarkdownEditorProps & { monaco: MonacoModule; from: Carry | null }) {
  const { resolved } = useTheme();
  const host = useRef<HTMLDivElement>(null);
  const instance = useRef<{ editor: editor.IStandaloneCodeEditor; model: editor.ITextModel } | null>(null);
  // The subscription is made once, so it reads what is current through these.
  const onChangeRef = useRef(onChange);
  const rowsRef = useRef(rows);
  const applying = useRef(false);
  // What the editor opens on. Read once, by the effect that creates it: after
  // that `value` is applied by its own effect, and the rest is options.
  const opening = useRef({ value, placeholder, ariaLabel });

  useEffect(() => {
    onChangeRef.current = onChange;
    rowsRef.current = rows;
  });

  // Re-themed in a passive effect, which is what makes the tokens it reads
  // current: ThemeProvider writes <html data-theme> in a layout effect, and
  // every layout effect in a commit runs before any passive one. Declared before
  // the mount effect so an editor is created in the theme already on screen.
  useEffect(() => applyTheme(monaco, resolved), [monaco, resolved]);

  useEffect(() => {
    const el = host.current;
    if (!el) return;

    // Font and size come from the host's CSS rather than from parsing tokens:
    // a computed value is px whatever unit the token is written in.
    const style = getComputedStyle(el);
    const fontSize = px(style.fontSize);
    const lineHeight = px(style.lineHeight);
    const padY = px(style.getPropertyValue('--sp-2'));
    const padX = px(style.getPropertyValue('--sp-3'));
    const chrome = el.offsetHeight - el.clientHeight;

    // Monaco lays out against the host's size when it is created, and an
    // element holding only an absolutely-positioned child has none.
    el.style.height = `${rowsRef.current * (lineHeight || fontSize * 1.5) + 2 * padY + chrome}px`;

    const model = monaco.editor.createModel(lf(opening.current.value), 'markdown');
    const code = monaco.editor.create(el, {
      model,
      automaticLayout: true,
      fontFamily: style.fontFamily,
      fontSize,
      lineHeight,
      padding: { top: padY, bottom: padY },
      lineDecorationsWidth: padX,
      lineNumbersMinChars: 0,
      lineNumbers: 'off',
      glyphMargin: false,
      folding: false,
      minimap: { enabled: false },
      overviewRulerLanes: 0,
      hideCursorInOverviewRuler: true,
      renderLineHighlight: 'none',
      wordWrap: 'on',
      scrollBeyondLastLine: false,
      scrollbar: { alwaysConsumeMouseWheel: false, horizontal: 'hidden' },
      quickSuggestions: false,
      suggestOnTriggerCharacters: false,
      wordBasedSuggestions: 'off',
      occurrencesHighlight: 'off',
      matchBrackets: 'never',
      guides: { indentation: false },
      bracketPairColorization: { enabled: false },
      unicodeHighlight: { ambiguousCharacters: false, invisibleCharacters: false },
      contextmenu: false,
      // A three-row box is shorter than the find widget; this makes room.
      find: { addExtraSpaceOnTop: true },
      placeholder: opening.current.placeholder,
      ariaLabel: opening.current.ariaLabel,
    });
    instance.current = { editor: code, model };

    // Grows with the text between the height it opened at and the ceiling its
    // place has, then scrolls inside itself. `max-height` is the host's, and CSS
    // holds it even where the computed value is a calc() that cannot be read as
    // px here - the ceiling read is an optimisation of that, not the guarantee.
    const fit = () => {
      const ceiling = parseCeiling(getComputedStyle(el).maxHeight);
      const floor = rowsRef.current * code.getOption(monaco.editor.EditorOption.lineHeight) + 2 * padY + chrome;
      el.style.height = `${clampedHeight(floor, ceiling, code.getContentHeight(), chrome)}px`;
    };
    fit();

    const subscriptions = [
      code.onDidContentSizeChange(fit),
      code.onDidChangeModelContent(() => {
        if (!applying.current) onChangeRef.current(model.getValue());
      }),
    ];
    const unlisten = focusOnLabelClick(el, () => code.focus());

    // Somebody who began typing in the plain box keeps their caret.
    if (from) {
      const start = model.getPositionAt(from.start);
      const end = model.getPositionAt(from.end);
      code.setSelection({
        selectionStartLineNumber: start.lineNumber,
        selectionStartColumn: start.column,
        positionLineNumber: end.lineNumber,
        positionColumn: end.column,
      });
      if (from.focused) code.focus();
    }

    return () => {
      unlisten();
      for (const s of subscriptions) s.dispose();
      code.dispose();
      model.dispose();
      instance.current = null;
      el.style.height = '';
    };
    // Made once per mount: everything that changes after is applied by the
    // effects below.
  }, [monaco, from]);

  // A new `value` - a save landing, a send emptying the box, a Revert - goes in
  // behind the flag, so it is not reported back as if it had been typed.
  useEffect(() => {
    const open = instance.current;
    const next = lf(value);
    if (!open || open.model.getValue() === next) return;
    applying.current = true;
    try {
      open.model.setValue(next);
    } finally {
      applying.current = false;
    }
  }, [value]);

  useEffect(() => {
    instance.current?.editor.updateOptions({ placeholder, ariaLabel });
  }, [placeholder, ariaLabel]);

  return <div ref={host} className={`hatch-md-editor${className ? ` ${className}` : ''}`} />;
}
