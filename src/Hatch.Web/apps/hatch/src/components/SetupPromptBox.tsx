import { setupPrompt, stampedSetupGuide } from '../lib/setupPrompt';
import { Command } from './Command';

/**
 * The line at the top of the Docs page, for whoever is reading it to pass on.
 * Not on a docs page: the address is the build's (see lib/setupPrompt.ts),
 * and a markdown file shipped in the repository cannot know it. A build
 * stamped with no address draws nothing here.
 */
export function DocsSetupPrompt() {
  return <SetupPromptBox prompt={setupPrompt(stampedSetupGuide())} />;
}

export function SetupPromptBox({ prompt }: { prompt: string | null }) {
  if (prompt === null) return null;
  return (
    <aside className="hatch-docs-setup" aria-label="Setup prompt">
      <p className="hatch-docs-setup__lead">
        <strong>Bringing somebody on?</strong> Send them this line to paste into Claude Code. Claude walks them from a
        bare machine to their own board, in plain words, and hands them every sign-in.
      </p>
      <Command command={prompt} />
    </aside>
  );
}
