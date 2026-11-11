import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Button, useTheme } from '@hatch/ui';
import { clientLogger } from '../lib/clientLogger';

type MonacoModule = typeof import('../lib/monaco');

/** A second copy of MarkdownEditor's private cache, deliberately: sharing it
    would take a third module. */
let loaded: MonacoModule | null = null;
let loading: Promise<MonacoModule> | null = null;

function loadMonaco(): Promise<MonacoModule> {
  loading ??= import('../lib/monaco').then((m) => (loaded = m));
  // A failed fetch must be retryable: the next panel to ask starts over.
  loading.catch(() => (loading = null));
  return loading;
}

/** The prescribed text beside the operator's own, differences marked. Two plain
    read-only boxes until the editor chunk lands, and if it never does. */
export function PromptDiff({ defaultPrompt, prompt }: { defaultPrompt: string; prompt: string }) {
  const [monaco, setMonaco] = useState<MonacoModule | null>(loaded);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (monaco) return;
    let live = true;
    loadMonaco().then(
      (m) => {
        if (live) setMonaco(m);
      },
      (e: unknown) => {
        clientLogger.error('The diff editor failed to load', { message: String(e) });
        if (live) setFailed(true);
      },
    );
    return () => {
      live = false;
    };
  }, [monaco]);

  if (!monaco) {
    return (
      <div className="hatch-playbook-prompt-diff__plain">
        <textarea readOnly rows={8} aria-label="Prescribed text" value={defaultPrompt} />
        <textarea readOnly rows={8} aria-label="The operator&apos;s own wording" value={prompt} />
        {failed && (
          <p className="hatch-md-editor__failed">
            The editor didn&rsquo;t load. Hatch may have been updated since this page was opened.{' '}
            <Button onClick={() => window.location.reload()}>Reload</Button>
          </p>
        )}
      </div>
    );
  }
  return <MonacoDiff monaco={monaco} defaultPrompt={defaultPrompt} prompt={prompt} />;
}

function MonacoDiff({ monaco, defaultPrompt, prompt }: { monaco: MonacoModule; defaultPrompt: string; prompt: string }) {
  const host = useRef<HTMLDivElement>(null);
  const { resolved, palette } = useTheme();

  // Passive, as in MarkdownEditor: the theme tokens are settled by then.
  useEffect(() => monaco.applyTheme(resolved), [monaco, resolved, palette]);

  useLayoutEffect(() => {
    const el = host.current;
    if (!el) return;
    monaco.applyTheme(resolved);
    const diffEditor = monaco.editor.createDiffEditor(el, {
      readOnly: true,
      originalEditable: false,
      renderSideBySide: true,
      automaticLayout: true,
    });
    diffEditor.setModel({
      original: monaco.editor.createModel(defaultPrompt, 'markdown'),
      modified: monaco.editor.createModel(prompt, 'markdown'),
    });
    return () => {
      const model = diffEditor.getModel();
      model?.original.dispose();
      model?.modified.dispose();
      diffEditor.dispose();
    };
    // Once per mount: both sides are read-only, and the dialog's own `opened`
    // reset remounts the tree, this component included, on every open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return <div ref={host} className="hatch-md-editor hatch-playbook-prompt-diff__editor" />;
}
