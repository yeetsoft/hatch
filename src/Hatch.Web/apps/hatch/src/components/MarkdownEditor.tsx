/* The box prose is written in.

   A drop-in for the `<textarea>` at each place a person writes markdown: VS
   Code's own editor (Monaco) - several cursors, select-next-occurrence, move and
   copy lines, find and replace, markdown highlighting - dressed in the house
   tokens so it reads as a Hatch field that happens to be powerful.

   TWO BODIES, ONE COMPONENT. `PlainEditor` is today's textarea, and it is what
   is shown on a touch screen (Monaco does not support touch browsers) and until
   Monaco's chunk has arrived. `MonacoEditor` takes over when it has. The value
   is the caller's state, so the text carries across the swap; the caret and
   focus are carried too, because a box that is on the page when it loads
   requests the chunk from its first `focus`, which lands mid-typing.

   LOADING. Monaco is a few megabytes and most pages have no editor on them, so
   it is a chunk of its own (lib/monaco.ts is the only importer) and this file
   reaches it with a dynamic `import()`. By default it is asked for on mount,
   which is right for a box opened by a gesture - Edit, New issue, a row's
   Prompt. A box that is already there when the page loads passes `deferred`,
   and asks on the first `focus` or `pointerenter` instead, so the issue page
   pays nothing until somebody reaches for the comment box.

   It is in apps/hatch and not @hatch/ui: the ui barrel pulls every component's
   CSS into every app, and only this app writes prose. */

import { useEffect, useImperativeHandle, useLayoutEffect, useRef, useState, type Ref, type RefObject } from 'react';
import { useTheme } from '@hatch/ui';
import { clientLogger } from '../lib/clientLogger';
import { normalizeEol } from '../lib/text';
import { clampedHeight, parseCeiling, useAutoGrow } from '../lib/useAutoGrow';

type MonacoModule = typeof import('../lib/monaco');

/** Kept once it has arrived, so the second editor on a page - or the same one
    reopened - starts as an editor and does not flash the textarea first. */
let loaded: MonacoModule | null = null;
let loading: Promise<MonacoModule> | null = null;

function loadMonaco(): Promise<MonacoModule> {
  loading ??= import('../lib/monaco').then((m) => (loaded = m));
  // A failed fetch must be retryable: the next box to ask starts over.
  loading.catch(() => (loading = null));
  return loading;
}

/** Where the caret was in the textarea a Monaco editor replaces. */
interface Handoff {
  focused: boolean;
  start: number;
  end: number;
}

interface PlainHandle {
  capture(): Handoff | null;
}

export interface MarkdownEditorProps {
  value: string;
  onChange: (next: string) => void;
  /** The height it opens at, and the floor it never goes back under. */
  rows: number;
  /** Where the ceiling comes from: its `max-height` bounds how far it grows. */
  className?: string;
  placeholder?: string;
  /** What a screen reader calls it. Inside a `Field`, that field's label text. */
  ariaLabel: string;
  /** The box is on the page when it loads: ask for the editor only once it is
      focused or pointed at. */
  deferred?: boolean;
}

export function MarkdownEditor(props: MarkdownEditorProps) {
  // Decided once per mount. A tablet with a trackpad reports a coarse primary
  // pointer and gets the textarea too - said in HA-56, not a bug.
  const [coarse] = useState(() => window.matchMedia('(pointer: coarse)').matches);
  const [ready, setReady] = useState<{ monaco: MonacoModule; handoff: Handoff | null } | null>(() =>
    !coarse && loaded ? { monaco: loaded, handoff: null } : null,
  );
  const [wanted, setWanted] = useState(!props.deferred);
  const plain = useRef<PlainHandle>(null);

  useEffect(() => {
    if (coarse || !wanted || ready) return;
    let live = true;
    loadMonaco().then(
      (monaco) => {
        if (live) setReady({ monaco, handoff: plain.current?.capture() ?? null });
      },
      // The textarea it is still showing works; say why it never changed.
      (e: unknown) => clientLogger.error('The editor failed to load', { message: String(e) }),
    );
    return () => {
      live = false;
    };
  }, [coarse, wanted, ready]);

  if (!ready) return <PlainEditor {...props} handle={plain} onWanted={() => setWanted(true)} />;
  return <MonacoEditor {...props} monaco={ready.monaco} handoff={ready.handoff} />;
}

/** Today's textarea, exactly. */
function PlainEditor({
  value,
  onChange,
  rows,
  className,
  placeholder,
  ariaLabel,
  handle,
  onWanted,
}: MarkdownEditorProps & { handle: Ref<PlainHandle>; onWanted: () => void }) {
  const ref = useAutoGrow(value);
  useFocusFromLabel(ref, () => ref.current?.focus());

  // What the parent asks for at the moment the editor arrives: where the caret
  // is in this box, so the one that replaces it can put it back.
  useImperativeHandle(handle, () => ({
    capture() {
      const el = ref.current;
      return el ? { focused: document.activeElement === el, start: el.selectionStart, end: el.selectionEnd } : null;
    },
  }));

  return (
    <textarea
      ref={ref}
      className={`hatch-description-editor${className ? ` ${className}` : ''}`}
      rows={rows}
      value={value}
      placeholder={placeholder}
      aria-label={ariaLabel}
      onChange={(e) => onChange(e.target.value)}
      onFocus={onWanted}
      onPointerEnter={onWanted}
    />
  );
}

/**
 * A click on the field's label puts the caret in the editor.
 *
 * Sites render their `Field` with `as="div"`, so there is no `<label>` to do
 * this: a `<label>` picks the first *labelable* descendant, which under Chromium
 * is not Monaco's EditContext `<div>` and under Firefox and Safari is Monaco's
 * 1px input area - a different control per browser. So the browser is taken out
 * of it. This reaches into `Field`'s own class names (`packages/ui`'s Field.tsx),
 * the one coupling to that markup; where there is no `Field` there is no label
 * to click and this does nothing.
 */
function useFocusFromLabel(el: RefObject<HTMLElement | null>, focus: () => void) {
  const latest = useRef(focus);
  useEffect(() => {
    latest.current = focus;
  });
  useEffect(() => {
    const label = el.current?.closest('.hatch-field__control')?.querySelector('.hatch-field__label');
    if (!label) return;
    const onClick = () => latest.current();
    label.addEventListener('click', onClick);
    return () => label.removeEventListener('click', onClick);
  }, [el]);
}

const px = (custom: string, fallback: number) => {
  const n = parseFloat(getComputedStyle(document.documentElement).getPropertyValue(custom));
  return Number.isFinite(n) ? n : fallback;
};

function MonacoEditor({
  monaco,
  handoff,
  value,
  onChange,
  rows,
  className,
  placeholder,
  ariaLabel,
}: MarkdownEditorProps & { monaco: MonacoModule; handoff: Handoff | null }) {
  const host = useRef<HTMLDivElement>(null);
  const editorRef = useRef<ReturnType<MonacoModule['editor']['create']> | null>(null);
  const modelRef = useRef<ReturnType<MonacoModule['editor']['createModel']> | null>(null);
  const applying = useRef(false);
  const onChangeRef = useRef(onChange);
  useEffect(() => {
    onChangeRef.current = onChange;
  });
  const { resolved } = useTheme();

  const { EndOfLinePreference, EditorOption } = monaco.editor;

  // Re-colours an open editor when the theme flips. Passive, on purpose:
  // ThemeProvider writes `<html data-theme>` in a layout effect, and every layout
  // effect in a commit runs before any passive one - so the tokens read here are
  // the new theme's. A layout effect would read the old ones. (The first colouring
  // is done below, before the editor exists; there the tokens are long settled.)
  useEffect(() => monaco.applyTheme(resolved), [monaco, resolved]);

  useFocusFromLabel(host, () => editorRef.current?.focus());

  // rows, ariaLabel, placeholder and the initial value are read once: a site that
  // changed the first three on a live editor would be a new feature, and none
  // does.
  useLayoutEffect(() => {
    const el = host.current;
    if (!el) return;
    const style = getComputedStyle(el);
    const lineHeight = parseFloat(style.lineHeight);
    const padding = px('--sp-2', 8);
    const chrome = el.offsetHeight - el.clientHeight || 2;

    // A box with no height has no width to wrap to; open at the floor and let
    // the content size say the rest.
    el.style.height = `${rows * (Number.isFinite(lineHeight) ? lineHeight : 21) + 2 * padding + chrome}px`;

    // A model of our own, created from the LF form: line endings are the
    // stored text's business and never the editor's.
    monaco.applyTheme(resolved);
    const model = monaco.editor.createModel(normalizeEol(value), 'markdown');
    const editor = monaco.editor.create(el, {
      model,
      automaticLayout: true,
      fontFamily: style.fontFamily,
      fontSize: parseFloat(style.fontSize),
      lineHeight: Number.isFinite(lineHeight) ? lineHeight : 0,
      minimap: { enabled: false },
      lineNumbers: 'off',
      glyphMargin: false,
      folding: false,
      // The room a field's text has from its border, which a gutter of
      // decorations would otherwise be.
      lineDecorationsWidth: px('--sp-3', 12),
      lineNumbersMinChars: 0,
      overviewRulerLanes: 0,
      hideCursorInOverviewRuler: true,
      overviewRulerBorder: false,
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
      padding: { top: padding, bottom: padding },
      // A three-row box is shorter than the find widget; this makes room for it.
      find: { addExtraSpaceOnTop: true },
      placeholder,
      ariaLabel,
    });
    editorRef.current = editor;
    modelRef.current = model;

    // Carry the caret over from the textarea this replaces.
    if (handoff) {
      const a = model.getPositionAt(handoff.start);
      const b = model.getPositionAt(handoff.end);
      editor.setSelection({
        selectionStartLineNumber: a.lineNumber,
        selectionStartColumn: a.column,
        positionLineNumber: b.lineNumber,
        positionColumn: b.column,
      });
      if (handoff.focused) editor.focus();
    }

    const fit = () => {
      const floor = rows * editor.getOption(EditorOption.lineHeight) + 2 * padding + chrome;
      const ceiling = parseCeiling(getComputedStyle(el).maxHeight);
      el.style.height = `${clampedHeight(floor, ceiling, editor.getContentHeight(), chrome)}px`;
    };
    fit();
    const sized = editor.onDidContentSizeChange(fit);
    // The ceiling is written against the screen; `automaticLayout` watches the
    // box and not the viewport.
    window.addEventListener('resize', fit);

    const changed = model.onDidChangeContent(() => {
      if (applying.current) return;
      onChangeRef.current(model.getValue(EndOfLinePreference.LF));
    });

    return () => {
      window.removeEventListener('resize', fit);
      changed.dispose();
      sized.dispose();
      editor.dispose();
      model.dispose();
      editorRef.current = null;
      modelRef.current = null;
    };
    // Once per mount, on purpose: see the note above the effect.
  }, []);

  // A value the editor did not write itself - a save landing, a send emptying
  // the box, Revert - goes in behind `applying`, so it is not echoed back as an
  // edit. Compared as LF, so line endings alone never count as a change.
  useEffect(() => {
    const model = modelRef.current;
    if (!model) return;
    const next = normalizeEol(value);
    if (next === model.getValue(EndOfLinePreference.LF)) return;
    applying.current = true;
    try {
      model.setValue(next);
    } finally {
      applying.current = false;
    }
  }, [value, EndOfLinePreference]);

  return <div ref={host} className={`hatch-md-editor${className ? ` ${className}` : ''}`} />;
}
