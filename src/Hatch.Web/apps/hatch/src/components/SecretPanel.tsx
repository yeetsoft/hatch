import { Button, Card } from '@hatch/ui';
import { Command } from './Command';
import type { RunnerSecret } from '../lib/runnerKey';

/**
 * "The secret panel" in docs/runners-page-design.md: a newly-minted or
 * rotated secret, shown once, at the top of the runner's detail - on the
 * phone as on the desk.
 *
 * `secret` lives only in `RunnersLayout`'s `useState`, so `Done` dismissing
 * it is the whole of "cleared"; nothing here holds a second copy.
 */
export function SecretPanel({ secret, onDone }: { secret: RunnerSecret; onDone: () => void }) {
  // Read at render rather than held, as RunnerPage.tsx does: the browser is
  // already standing at the right origin. Guarded for a test environment
  // with no window - this app's tests run in plain Node, with no jsdom.
  const origin = typeof window === 'undefined' ? '' : window.location.origin;

  return (
    <Card>
      <h2 className="hatch-section-title">New secret for {secret.name}</h2>
      <p className="text-muted">
        Shown once. Copy it now: it is kept nowhere you can read it, and reloading this page loses it.
      </p>
      <div className="hatch-runner-commands">
        <Command command={`hatch config --origin ${origin}`} />
        <Command command={`hatch config --key ${secret.secret}`} />
      </div>
      {secret.rotated && <p className="text-muted">The old secret stopped working when this was made.</p>}
      <p className="text-muted">
        Or run <code>hatch config</code> and paste the key at the prompt, which keeps it out of your shell history.
      </p>
      <div className="hatch-form-actions">
        <Button onClick={onDone}>Done</Button>
      </div>
    </Card>
  );
}
