/* The editor's code, and the only file that imports it.

   Nothing else may import `monaco-editor` - not statically, not with
   `import()` - because this module is the seam Vite cuts the chunk along:
   `MarkdownEditor` reaches it with `import('../lib/monaco')`, and that is what
   keeps a few megabytes of script off every page that has no editor on it. A
   second importer drags Monaco into whatever chunk it lives in. `oxlint` will
   not catch that; this header is the whole of the rule. (A type-only import, as
   `editorTheme.ts` has, is erased and is fine.)

   Why these paths and not `import 'monaco-editor'`. The bare import is
   `editor.main`, which registers ~80 languages, the TypeScript, CSS, HTML and
   JSON services and a language-server client - all of it for a box that writes
   markdown. So this takes the core (`editor.api`) and adds, one by one, the
   contributions a prose editor uses. What is *not* registered is as deliberate
   as what is: `suggest`, `hover`, `codeAction`, `links`, `unicodeHighlighter`,
   `wordHighlighter`, `bracketMatching`, `contextmenu` and `comment`. Most of
   "nothing pops up while typing prose" is that omission; the options at the
   call site are belt and braces.

   Why the version is exact. `features/<name>/register` and
   `languages/definitions/<name>/register` are not a documented API and have
   moved between minors. `package.json` pins `0.57.0` with no caret so that an
   install cannot quietly move them; a bump is a change to read the tarball for.

   Why the worker is imported here and not fetched. `?worker` makes Vite emit it
   under `assets/` and serve it from Hatch's own origin. The loader that
   `@monaco-editor/react` uses fetches Monaco from a CDN, which is the
   third-party dependency the house declines for its own font (tokens.css §1);
   with the WAN down, every editor still opens. The find widget's icons are a
   font emitted the same way, from the CSS that `codicon/register` imports. */

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

self.MonacoEnvironment = { getWorker: () => new EditorWorker() };

export { editor } from 'monaco-editor/editor/editor.api';
