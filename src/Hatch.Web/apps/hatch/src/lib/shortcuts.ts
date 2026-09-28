/* The keyboard, as functions of plain data - no DOM, so they are tested in
   node like the rest of lib/. */

interface KeyChord {
  key: string;
  metaKey: boolean;
  ctrlKey: boolean;
  shiftKey: boolean;
  altKey: boolean;
}

/**
 * Whether this keystroke is Undo: `z` with Meta or Ctrl, and neither Shift nor
 * Alt (⇧⌘Z and Ctrl+Shift+Z are Redo, which nothing here answers).
 *
 * Either modifier is accepted on every platform, so nothing has to sniff which
 * one it is on: a Mac sends ⌘Z, and Windows and Linux send Ctrl+Z.
 */
export function isUndoShortcut(e: KeyChord): boolean {
  return (e.key === 'z' || e.key === 'Z') && (e.metaKey || e.ctrlKey) && !e.shiftKey && !e.altKey;
}

/**
 * Whether this keystroke opens the console: `/`, with none of Meta, Ctrl or Alt
 * held - those are the browser's and the platform's, and Ctrl+/ in particular is
 * somebody else's shortcut in most editors.
 *
 * Shift is allowed, because some layouts need it to type a slash. `?` arrives
 * as its own key and is not this.
 */
export function isGoToShortcut(e: KeyChord): boolean {
  return e.key === '/' && !e.metaKey && !e.ctrlKey && !e.altKey;
}

/** The input types that are a control rather than somewhere to type. */
const NOT_TEXT = new Set(['button', 'checkbox', 'color', 'file', 'image', 'radio', 'range', 'reset', 'submit']);

/**
 * Whether typing lands here, which is where Undo already means something: the
 * field's own text undo, which the board must not take from it.
 */
export function isTypingTarget(el: { tagName: string; isContentEditable?: boolean; type?: string } | null): boolean {
  if (!el) return false;
  if (el.isContentEditable) return true;

  const tag = el.tagName.toLowerCase();
  if (tag === 'textarea' || tag === 'select') return true;
  return tag === 'input' && !NOT_TEXT.has((el.type ?? 'text').toLowerCase());
}

/** What to call the shortcut on the operator's platform. Pass
    `navigator.userAgentData?.platform ?? navigator.platform`. */
export function undoShortcutLabel(platform: string): string {
  return /mac|iphone|ipad/i.test(platform) ? '⌘Z' : 'Ctrl+Z';
}

/** The platform the browser reports, or empty where there is no browser. */
export function currentPlatform(): string {
  if (typeof navigator === 'undefined') return '';
  const nav = navigator as Navigator & { userAgentData?: { platform?: string } };
  return nav.userAgentData?.platform ?? nav.platform ?? '';
}
