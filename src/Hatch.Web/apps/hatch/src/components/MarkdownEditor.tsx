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
   CSS into each consuming app, and only this app writes prose. */

import { useCallback, useEffect, useImperativeHandle, useLayoutEffect, useRef, useState, type Ref, type RefObject } from 'react';
import { Button, useTheme } from '@hatch/ui';
import { uploadAttachment } from '../api/client';
import { clientLogger } from '../lib/clientLogger';
import { imageMarkdown, insertText, pastedImage } from '../lib/descriptionImage';
import { message } from '../lib/errors';
import { shrink } from '../lib/shrinkImage';
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
  insertAtCaret(text: string): boolean;
}

interface MonacoHandle {
  insertAtCaret(text: string): boolean;
}

/** A way in, for a caller that needs to put text at the caret itself - a
    drop zone, say - rather than through `value`/`onChange`. */
export interface MarkdownEditorHandle {
  insertAtCaret(text: string): boolean;
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
  /** Meta/Ctrl+Enter pressed in the box. */
  onCtrlEnter?: () => void;
  /** A way in for a caller that needs to insert text at the caret itself. */
  handle?: Ref<MarkdownEditorHandle>;
}

export function MarkdownEditor(props: MarkdownEditorProps) {
  // Decided once per mount. A tablet with a trackpad reports a coarse primary
  // pointer and gets the textarea too - said in HA-56, not a bug.
  const [coarse] = useState(() => window.matchMedia('(pointer: coarse)').matches);
  const [ready, setReady] = useState<{ monaco: MonacoModule } | null>(() => (!coarse && loaded ? { monaco: loaded } : null));
  const [wanted, setWanted] = useState(!props.deferred);
  // Chrome remembers a failed dynamic import for the life of the page, so a
  // second attempt cannot succeed where the first didn't: this is what stays
  // true until a reload, not a retry flag.
  const [failed, setFailed] = useState(false);
  const plain = useRef<PlainHandle>(null);
  const monacoHandle = useRef<MonacoHandle>(null);
  // Where the caret was in the textarea, as of the commit that removes it -
  // not as of the moment the chunk resolved, which can be a render or two
  // earlier and a keystroke or two stale. `PlainEditor` writes this itself,
  // from a layout effect's cleanup, in the same commit `MonacoEditor` mounts in.
  const handoffRef = useRef<Handoff | null>(null);

  useImperativeHandle(props.handle, () => ({
    insertAtCaret: (text) => plain.current?.insertAtCaret(text) ?? monacoHandle.current?.insertAtCaret(text) ?? false,
  }));

  useEffect(() => {
    if (coarse || !wanted || ready) return;
    let live = true;
    loadMonaco().then(
      (monaco) => {
        if (live) setReady({ monaco });
      },
      // The textarea it is still showing works; say why it never changed.
      (e: unknown) => {
        clientLogger.error('The editor failed to load', { message: String(e) });
        if (live) setFailed(true);
      },
    );
    return () => {
      live = false;
    };
  }, [coarse, wanted, ready]);

  if (!ready) {
    return <PlainEditor {...props} bodyHandle={plain} onWanted={() => setWanted(true)} failed={failed} handoffRef={handoffRef} />;
  }
  return <MonacoEditor {...props} bodyHandle={monacoHandle} monaco={ready.monaco} handoffRef={handoffRef} />;
}

/**
 * Paste an image into either body of the editor.
 *
 * Nothing is inserted until the upload has succeeded, and then at the caret as
 * it is *then* - there is no placeholder to find again. While it is in flight
 * the note says so; on a refusal the note carries the server's sentence and the
 * draft is untouched. `onPaste` is stable, since Monaco's listener is attached
 * once; it reads the latest `insert` through a ref, as `onChangeRef` does.
 */
function useImagePaste(insert: (text: string) => void) {
  const insertRef = useRef(insert);
  useEffect(() => {
    insertRef.current = insert;
  });
  const [note, setNote] = useState<string | null>(null);

  const onPaste = useCallback((e: ClipboardEvent) => {
    const file = pastedImage(Array.from(e.clipboardData?.files ?? []));
    if (!file) return; // text, or nothing Hatch stores: the paste is left alone
    e.preventDefault();
    e.stopPropagation();
    setNote('Uploading image\u2026');
    shrink(file)
      .then((blob) => uploadAttachment(blob, file.name || 'image.png'))
      .then(
        ({ id }) => {
          insertRef.current(imageMarkdown(id));
          setNote(null);
        },
        (err: unknown) => setNote(message(err)),
      );
  }, []);

  return { onPaste, note };
}

/** Today's textarea, exactly. */
function PlainEditor({
  value,
  onChange,
  rows,
  className,
  placeholder,
  ariaLabel,
  onCtrlEnter,
  bodyHandle,
  onWanted,
  failed,
  handoffRef,
}: MarkdownEditorProps & { bodyHandle: Ref<PlainHandle>; onWanted: () => void; failed: boolean; handoffRef: RefObject<Handoff | null> }) {
  const ref = useAutoGrow(value);
  useFocusFromLabel(ref, () => ref.current?.focus());

  // Where the caret was, as late as it can be read: a layout effect's cleanup
  // runs in the commit that unmounts this textarea, which is the same commit
  // `MonacoEditor` mounts in - after whatever was typed in between.
  useLayoutEffect(() => {
    return () => {
      const el = ref.current;
      if (el) handoffRef.current = { focused: document.activeElement === el, start: el.selectionStart, end: el.selectionEnd };
    };
  }, [ref, handoffRef]);

  const insertAtCaret = (text: string) => {
    const el = ref.current;
    if (!el) return;
    const { value: next, caret } = insertText(el.value, el.selectionStart, el.selectionEnd, text);
    onChange(next);
    // The textarea is controlled: the caret can only be put back once React has
    // written the new value.
    requestAnimationFrame(() => {
      el.focus();
      el.setSelectionRange(caret, caret);
    });
  };

  const image = useImagePaste(insertAtCaret);

  // The way in for a caller that wants to insert text at the caret itself.
  useImperativeHandle(bodyHandle, () => ({
    insertAtCaret(text) {
      if (document.activeElement !== ref.current) return false;
      insertAtCaret(text);
      return true;
    },
  }));

  return (
    <>
      <textarea
        ref={ref}
        className={`hatch-description-editor${className ? ` ${className}` : ''}`}
        rows={rows}
        value={value}
        placeholder={placeholder}
        aria-label={ariaLabel}
        onChange={(e) => onChange(e.target.value)}
        onPaste={(e) => image.onPaste(e.nativeEvent)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) onCtrlEnter?.();
        }}
        onFocus={onWanted}
        onPointerEnter={onWanted}
      />
      {/* The box still works - this says why it is plain, and offers the only
          thing that can fix it: Chrome's memory of the failed fetch outlives
          the page, so a reload is the retry. */}
      {image.note && <p className="hatch-md-editor__failed">{image.note}</p>}
      {failed && (
        <p className="hatch-md-editor__failed">
          The editor didn&rsquo;t load. Hatch may have been updated since this page was opened.{' '}
          <Button onClick={() => window.location.reload()}>Reload</Button>
        </p>
      )}
    </>
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
  handoffRef,
  value,
  onChange,
  rows,
  className,
  placeholder,
  ariaLabel,
  onCtrlEnter,
  bodyHandle,
}: MarkdownEditorProps & { monaco: MonacoModule; handoffRef: RefObject<Handoff | null>; bodyHandle: Ref<MonacoHandle> }) {
  const host = useRef<HTMLDivElement>(null);
  const editorRef = useRef<ReturnType<MonacoModule['editor']['create']> | null>(null);
  const modelRef = useRef<ReturnType<MonacoModule['editor']['createModel']> | null>(null);
  const applying = useRef(false);
  const onChangeRef = useRef(onChange);
  useEffect(() => {
    onChangeRef.current = onChange;
  });
  const onCtrlEnterRef = useRef(onCtrlEnter);
  useEffect(() => {
    onCtrlEnterRef.current = onCtrlEnter;
  });
  const { resolved, palette } = useTheme();

  const executeInsert = (text: string) => {
    const editor = editorRef.current;
    const selection = editor?.getSelection();
    // onDidChangeContent reports the edit through onChange, as a keystroke is.
    if (editor && selection) editor.executeEdits('image-paste', [{ range: selection, text, forceMoveMarkers: true }]);
  };

  const image = useImagePaste(executeInsert);

  useImperativeHandle(bodyHandle, () => ({
    insertAtCaret(text) {
      if (!editorRef.current?.hasTextFocus()) return false;
      executeInsert(text);
      return true;
    },
  }));

  const { EndOfLinePreference, EditorOption } = monaco.editor;

  // Re-colours an open editor when the theme flips or a palette is applied. Passive, on purpose:
  // ThemeProvider writes `<html data-theme>` in a layout effect, and every layout
  // effect in a commit runs before any passive one - so the tokens read here are
  // the new theme's. A layout effect would read the old ones. (The first colouring
  // is done below, before the editor exists; there the tokens are long settled.)
  useEffect(() => monaco.applyTheme(resolved), [monaco, resolved, palette]);

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

    // Only where a caller asked: an unconditional command would swallow
    // Monaco's own Ctrl+Enter (insert line below) in every other box. The key
    // enums come off the loaded module, not a static import of lib/monaco.ts,
    // which would put the editor in this page's chunk.
    if (onCtrlEnterRef.current) {
      editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => onCtrlEnterRef.current?.());
    }

    // Carry the caret over from the textarea this replaces. Read fresh, not
    // from a prop fixed when the chunk resolved: this layout effect and the
    // textarea's own unmount cleanup run in the same commit, so this is the
    // caret as of the swap, not as of whenever the import settled.
    const handoff = handoffRef.current;
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

    // Capture phase, on the host: Monaco's own paste handler sits on an element
    // inside it and would otherwise read the clipboard as text first.
    el.addEventListener('paste', image.onPaste, true);

    const changed = model.onDidChangeContent(() => {
      if (applying.current) return;
      onChangeRef.current(model.getValue(EndOfLinePreference.LF));
    });

    return () => {
      window.removeEventListener('resize', fit);
      el.removeEventListener('paste', image.onPaste, true);
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
  //
  // A layout effect, not a passive one: it must run inside the commit a
  // keystroke's onChange triggers, before the browser can hand Monaco another
  // one. A passive effect runs in a later task - widest when it is batched
  // into a re-render of the whole board - and by the time it runs `value` may
  // already be a keystroke stale against the model. Writing a stale value is
  // model.setValue(), which Monaco treats as a flush: every cursor resets to
  // line 1, column 1, and the undo stack is dropped.
  useLayoutEffect(() => {
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

  return (
    <>
      <div ref={host} className={`hatch-md-editor${className ? ` ${className}` : ''}`} />
      {image.note && <p className="hatch-md-editor__failed">{image.note}</p>}
    </>
  );
}
