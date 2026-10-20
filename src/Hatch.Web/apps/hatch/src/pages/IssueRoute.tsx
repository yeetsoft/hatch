import { useParams } from 'react-router-dom';
import { IssuePage } from './IssuePage';

/* What `/issues/:key` mounts. Without a key React Router keeps one IssuePage
   for every issue the route reaches, so a child, parent or blocker link on the
   page - or the redirect after a move - would draw the last issue's title,
   comments and history under the new URL until the new read landed, leave it
   there with an error line if that read failed, and let a late focus refresh
   for the old one paint over the new. Keyed by the issue, each one starts from
   "Loading…" with nothing of the last one's left, and an answer still in
   flight for the last lands on a page that is gone. */
export function IssueRoute() {
  const { key = '' } = useParams();
  return <IssuePage key={key} />;
}
