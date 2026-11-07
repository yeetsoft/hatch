import { Button, Card } from '@hatch/ui';
import { scopeWithProject } from '../lib/scope';
import { useScope } from '../lib/useScope';

/**
 * Said on an issue the tab's scope does not reach - the page was opened by a
 * link, or the scope narrowed under it - with the one press that widens the
 * scope by that issue's project. Nothing at all when the tab is unscoped or the
 * project is in scope, which is nearly always, so it costs those pages nothing.
 *
 * Reads `useScope()` itself, as `IssueSubheader` does, rather than having the
 * scope threaded in. The press hands `setScope` the keys plus one, raw; the
 * scope's own writer orders them and drops the param when every project is
 * named.
 */
export function IssueScopeNotice({ projectKey }: { projectKey: string }) {
  const { isScoped, inScope, keys, setScope } = useScope();
  if (!isScoped || inScope(projectKey)) return null;

  return (
    <Card className="hatch-scope-notice">
      <p className="hatch-scope-notice-line">
        <code>{projectKey}</code> is outside this tab&rsquo;s scope.
      </p>
      <div className="hatch-form-actions">
        <Button onClick={() => setScope(scopeWithProject(keys, projectKey))}>
          Add <code>{projectKey}</code> to this tab
        </Button>
      </div>
    </Card>
  );
}
