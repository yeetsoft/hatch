import { useMenuOpen } from '@hatch/ui';
import { useRevision } from '../lib/useRevision';
import { BuildStamp } from './BuildStamp';

/**
 * The build stamp's own file so App.tsx holds one element instead of two
 * hooks and a prop - the same reason `NavUtilization` exists.
 */
export function NavBuild() {
  const open = useMenuOpen();
  const revision = useRevision(open);
  return <BuildStamp revision={revision} />;
}
