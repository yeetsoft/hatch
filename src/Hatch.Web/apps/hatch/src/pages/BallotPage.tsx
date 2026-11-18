import { useCallback, useEffect, useRef } from 'react';
import { PageHeader } from '@hatch/ui';
import { getBallot } from '../api/client';
import { BallotTree } from '../components/BallotTree';
import { ScopeGate } from '../components/ScopeGate';
import { useAttentionContext } from '../lib/useAttentionContext';
import { useAttentionNow } from '../lib/useAttentionNow';
import { useLoaded } from '../lib/useLoaded';
import { useScope } from '../lib/useScope';
import type { Ballot } from '../types';

interface BallotView {
  ballot: Ballot;
  /** The scope this was fetched under, in the form `scopeKey` below takes. */
  scopeKey: string;
}

/**
 * Every question waiting on a person, grouped under the issues that enclose
 * it, with enough of each issue to answer from.
 *
 * It narrows to the tab's scope as `PlanPage` does, and re-reads when the
 * attention provider does, so a question answered anywhere else leaves it.
 */
export function BallotPage() {
  return (
    <ScopeGate>
      <BallotContent />
    </ScopeGate>
  );
}

function BallotContent() {
  const scope = useScope();
  const { attention } = useAttentionContext();
  const now = useAttentionNow(true);

  /* The scope in force, as one string: `scope.ids` is empty until the
     provider's own project list lands, so a ballot fetched before that was
     fetched unscoped, and this is what tells the two apart. */
  const scopeKey = scope.isScoped ? scope.keys.join(',') : '';

  const load = useCallback(
    async (): Promise<BallotView> => ({
      ballot: await getBallot(scope.isScoped ? scope.ids : undefined),
      scopeKey,
    }),
    [scope.isScoped, scope.ids, scopeKey],
  );

  const { data, error, reload } = useLoaded<BallotView>(load);

  /* The provider's answer changing is the signal that something waiting on a
     person moved. Not the first run: the load above is already that read. */
  const seen = useRef(attention);
  useEffect(() => {
    if (seen.current === attention) return;
    seen.current = attention;
    void reload();
  }, [attention, reload]);

  /* Only a ballot fetched for the scope now in force is drawn, so an unscoped
     read that lands before the project list never flashes another project's
     questions. */
  const current = data !== null && scope.loaded && data.scopeKey === scopeKey;

  if (error && !data) return <p className="text-danger">{error}</p>;
  if (!data || !current) return <p className="text-muted">Loading…</p>;

  return (
    <div className="hatch-page">
      <PageHeader
        title="Ballot"
        description="Every question waiting on an answer, under the issue it was asked on."
      />

      {error && <p className="text-danger">{error}</p>}

      <BallotTree roots={data.ballot.roots} projects={scope.allProjects} now={now} />
    </div>
  );
}
