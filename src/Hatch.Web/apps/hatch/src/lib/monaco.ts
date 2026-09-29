/* Monaco, and only as much of it as a markdown box needs.

   THIS IS THE ONLY MODULE THAT MAY IMPORT `monaco-editor`, and it is only ever
   reached as `import('../lib/monaco')` from components/MarkdownEditor.tsx. That
   dynamic import is what makes Vite give the editor a chunk of its own, so a
   page that has no editor on it never downloads one (HA-56, criterion 14). A
   second importer - static, or dynamic from somewhere else - would drag the
   editor into whatever chunk it sits in. Nothing enforces this: oxlint has no
   rule for it, so it is said here. (`import type` is fine; it is erased.)

   This chunk must import nothing the page's own chunk also imports. A `lib/`
   helper shared with the page - say `editorTheme.ts` reaching for `lib/color.ts`
   - would be hoisted into the entry chunk, and this chunk's only import
   (Vite's preload helper) would stop being the whole story: Rolldown would add
   a second import naming the shared chunk, and that chunk's name follows the
   entry's, which is exactly the coupling vite.config.ts's `codeSplitting`
   group exists to break for the preload helper alone. `editorChunk.test.ts` is
   the guard that catches it.

   Not the bare `monaco-editor`: its entry registers ~80 languages, the
   TypeScript, CSS, HTML and JSON services and a language-server client, none of
   which a comment box wants. `editor.api` is the core with no contributions,
   and each `features/<name>/register` below opts one back in. What is left out is
   most of "nothing pops up while typing prose" (criterion 4): no suggest, hover,
   code action, links, unicode highlighting, word highlighting, bracket matching,
   context menu or comment toggling.

   THE VERSION IS PINNED EXACTLY in package.json, no caret, for this file's sake.
   `features/<name>/register` and `languages/definitions/<name>` are reached through the
   package's `./*` export map, not through a documented API, and they have moved
   between minors. An upgrade is a decision, made with this list in front of it.

   Nothing comes from another host. The worker is bundled by Vite (`?worker`)
   and served from Hatch's own origin, and the find widget's icons are a font
   that only `features/codicon/register` pulls in - so with the WAN down and the
   LAN up, the editor still opens, highlighted, with its icons (criterion 13).
   `@monaco-editor/react` is not used for the opposite reason: its loader fetches
   Monaco from a CDN by default. */

import 'monaco-editor/features/multicursor/register';
import 'monaco-editor/features/find/register';
import 'monaco-editor/features/linesOperations/register';
import 'monaco-editor/features/wordOperations/register';
import 'monaco-editor/features/clipboard/register';
import 'monaco-editor/features/cursorUndo/register';
import 'monaco-editor/features/caretOperations/register';
import 'monaco-editor/features/dnd/register';
import 'monaco-editor/features/placeholderText/register';
import 'monaco-editor/features/toggleTabFocusMode/register';
import 'monaco-editor/features/codicon/register';
import 'monaco-editor/languages/definitions/markdown/register';
import EditorWorker from 'monaco-editor/editor/editor.worker?worker';
import { editor } from 'monaco-editor/editor/editor.api';
import { EDITOR_TOKEN_NAMES, editorTheme, type EditorTokens } from './editorTheme';

globalThis.MonacoEnvironment = { getWorker: () => new EditorWorker() };

/* Applied once per resolved theme, and never redefined: `setTheme` returns
   early when handed the theme *object* it already has, so re-colouring is a
   second name, not a second definition of the first. It lives here, and not in
   the component, so the theme's mapping travels in this chunk rather than in
   every page's. */
const defined = new Set<string>();

/**
 * Colours every open editor in the house palette for `resolved`, reading the
 * tokens off the page at the moment of the call - so the caller must be at a
 * point where `<html data-theme>` is current.
 */
export function applyTheme(resolved: 'light' | 'dark'): void {
  const name = `hatch-${resolved}`;
  if (!defined.has(name)) {
    const style = getComputedStyle(document.documentElement);
    const tokens = Object.fromEntries(EDITOR_TOKEN_NAMES.map((n) => [n, style.getPropertyValue(n)])) as EditorTokens;
    editor.defineTheme(name, editorTheme(tokens, resolved === 'dark'));
    defined.add(name);
  }
  // Global to the page: every open editor follows, which is what a theme flip
  // wants.
  editor.setTheme(name);
}

export { editor };
