import { Link, useParams } from 'react-router-dom';
import { Button, EmptyState, PageHeader } from '@hatch/ui';
import { useRunnersOutlet } from './RunnersLayout';
import { usePhone } from '../lib/viewport';

/** The right pane's `:name` route, a stub: the runner's own header, or a
    not-found state. Reads the runner out of the layout's own poll, with no
    second fetch - see docs/runners-page-design.md, "Routes and layout". */
export function RunnerDetail() {
  const { name } = useParams();
  const { runners } = useRunnersOutlet();
  const phone = usePhone();

  if (runners === null) return null;

  const runner = runners.find((r) => r.name === name);

  if (!runner) {
    return (
      <EmptyState
        message={`No runner called ${name}.`}
        action={
          <Button as={Link} to="/runners">
            Runners
          </Button>
        }
      />
    );
  }

  if (phone) {
    return (
      <>
        <Button as={Link} to="/runners">
          <span aria-hidden="true">‹</span> Runners
        </Button>
        <PageHeader title={name} level={1} />
      </>
    );
  }

  return <PageHeader title={name} level={2} />;
}
