import { useMe } from '../lib/useMe';
import { AmbitionsPage } from './AmbitionsPage';
import { BoardPage } from './BoardPage';

/* What `/` and the unknown-route fallback both mount. Drawing nothing until
   the first Me read has settled keeps the board from flashing in ahead of
   a person whose home screen is Ambitions. */
export function HomeRoute() {
  const { me, settled } = useMe();
  if (!settled) return null;
  return me?.home === 'ambitions' ? <AmbitionsPage /> : <BoardPage />;
}
