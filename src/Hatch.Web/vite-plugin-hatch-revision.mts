/**
 * Stamps the build's `hatch-revision` into each app's index.html, and makes
 * the page send it back on every API call it makes.
 *
 * See docs/plans/version.md. Two things about this plugin are load-bearing and
 * neither is obvious from what it emits.
 *
 * **It writes into index.html, never into the bundle.** The obvious
 * implementation is a Vite `define` that inlines the sha as a constant, and it
 * would be wrong: Vite content-hashes everything under assets/, so a sha in the
 * bundle changes that bundle's hash on *every* commit, so a backend-only
 * deploy - by far the common case - would change every frontend's files for
 * a change they can't see. index.html carries no hash and is served
 * `no-cache` (Program.cs); a <meta> in it perturbs neither the bundle's
 * filenames nor the client's own check (which reads script[src]/link[href]).
 *
 * **The interceptor lives here rather than in each app's source.** There are
 * three apps (the board, the sign-in app and the design gallery), each with
 * its own clientLogger.ts; a runtime module would have become a fourth copy.
 * Emitting the behaviour from the one place that already has the value keeps
 * it single-sourced, and `head-prepend` gets it in front of the ui-logs ping that
 * index.html itself fires - so even that first request carries the header.
 *
 * The values come from the environment (Dockerfile.api passes build args
 * through; publish.yml computes them) rather than from git, so a local
 * `npm run build` needs no repository and honestly reports `dev`.
 *
 * **It also stamps where the setup guide is**, as `hatch-setup-guide`, when
 * the build was told (HATCH_SETUP_GUIDE; publish.yml asks GitHub for this
 * repository's Pages site). It is here because this is the one place a build
 * fact reaches index.html, and index.html rather than the bundle for the
 * reason above. Untold, there is no meta at all rather than a made-up one:
 * apps/hatch/src/lib/setupPrompt.ts reads its absence as "offer no line".
 *
 * **This file is ESM by its extension, not by its package.** It sits next to
 * the workspace root's package.json, which deliberately carries no `"type"`.
 * `.mts` (and the `.mjs` specifier the apps import it by) is always an ES
 * module regardless of the nearest package.json, so adding `"type": "module"`
 * up there would fix nothing and would change how every non-workspace `.js`
 * file under src/Hatch.Web is parsed.
 */

/** What a local build reports. Deliberately not a git call - see the header. */
const DEV_REVISION = 'dev'
const DEV_SEQUENCE = '0'

export interface HatchRevisionOptions {
  /** App directory name under wwwroot/apps, e.g. "hatch". Diagnostics only. */
  app: string
}

/**
 * Vite's own `Plugin` and `HtmlTagDescriptor` types, restated structurally
 * rather than imported. This file sits above the apps and so above their
 * node_modules trees - `import type { Plugin } from 'vite'` resolves from none
 * of them. Structural typing means the object still has to satisfy Vite's real
 * `PluginOption` at every call site, which is the check that actually matters;
 * this only has to be narrow enough not to widen `injectTo` to `string`.
 */
interface HtmlTag {
  tag: string
  attrs?: Record<string, string | boolean | undefined>
  children?: string
  injectTo?: 'head' | 'body' | 'head-prepend' | 'body-prepend'
}

interface RevisionPlugin {
  name: string
  transformIndexHtml(): HtmlTag[]
}

export function hatchRevision({ app }: HatchRevisionOptions): RevisionPlugin {
  const revision = process.env.HATCH_REVISION || DEV_REVISION
  const sequence = process.env.HATCH_SEQUENCE || DEV_SEQUENCE
  const setupGuide = process.env.HATCH_SETUP_GUIDE?.trim() || null

  return {
    name: 'hatch-revision',
    transformIndexHtml() {
      return [
        ...(setupGuide
          ? [
              {
                tag: 'meta',
                attrs: { name: 'hatch-setup-guide', content: setupGuide },
                injectTo: 'head-prepend' as const,
              },
            ]
          : []),
        {
          tag: 'meta',
          attrs: { name: 'hatch-revision', content: revision },
          injectTo: 'head-prepend',
        },
        {
          tag: 'meta',
          attrs: { name: 'hatch-sequence', content: sequence },
          injectTo: 'head-prepend',
        },
        {
          tag: 'meta',
          attrs: { name: 'hatch-app', content: app },
          injectTo: 'head-prepend',
        },
        {
          tag: 'script',
          children: bootstrap(revision, sequence),
          injectTo: 'head-prepend',
        },
      ]
    },
  }
}

/**
 * Reads the stamped values into globals the app's own code can pick up (the
 * same shape as window.__uiLogSessionId, which index.html already sets for
 * clientLogger.ts), then wraps fetch so outbound API calls identify the build
 * that made them.
 *
 * Same-origin only, and that is correctness rather than caution: a custom
 * header on a cross-origin request triggers a CORS preflight, turning one round
 * trip into two on a request that was working fine. Every Hatch app reaches its
 * API same-origin anyway (see docs/reverse-proxy-architecture.md).
 *
 * Written as plain, conservative JS on purpose: it is injected verbatim into
 * index.html, which no build step transpiles, so whatever is written here is
 * what a browser parses. The kiosk tablets exist because their stock WebView
 * (Chrome 74) could not parse the dashboard's bundle at all - this is not the
 * file in which to discover the next such limit.
 */
function bootstrap(revision: string, sequence: string): string {
  return `
(function () {
  try {
    window.__hatchRevision = ${JSON.stringify(revision)};
    window.__hatchSequence = ${JSON.stringify(sequence)};

    var nativeFetch = window.fetch;
    if (typeof nativeFetch !== 'function') return;

    window.fetch = function (input, init) {
      try {
        var url = typeof input === 'string' ? input : (input && input.url);
        // Relative URLs resolve against the document and are same-origin by
        // construction; an absolute one has to be checked.
        if (url && new URL(url, location.href).origin === location.origin) {
          var headers = new Headers((init && init.headers) || (typeof input !== 'string' && input && input.headers) || undefined);
          headers.set('Hatch-Client-Revision', window.__hatchRevision);
          headers.set('Hatch-Client-Sequence', window.__hatchSequence);
          // The kiosk shell tells the page its own revision (apps/kiosk); it is
          // a separate artifact from this bundle and drifts independently, so
          // it travels as its own header rather than overwriting this one.
          if (window.__hatchShellRevision) {
            headers.set('Hatch-Shell-Revision', window.__hatchShellRevision);
          }

          // A shallow copy rather than a mutation: init belongs to the caller,
          // and a caller that reuses one options object across several fetches
          // must not accumulate our headers into it.
          var next = {};
          for (var key in init) {
            if (Object.prototype.hasOwnProperty.call(init, key)) next[key] = init[key];
          }
          next.headers = headers;
          init = next;
        }
      } catch (e) {
        // A header is never worth failing a request over.
      }
      return nativeFetch.call(this, input, init);
    };
  } catch (e) {}
})();
`.trim()
}
