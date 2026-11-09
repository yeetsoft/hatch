import { Link } from 'react-router-dom';
import { Card, PageHeader, Text } from '@hatch/ui';

/** Every report there is, and the question each one answers. A new report is
    one more entry here, plus the route it points at. */
const REPORTS: { to: string; title: string; answers: string }[] = [
  {
    to: '/reports/timeline',
    title: 'Timeline',
    answers: 'When each epic was in flight, and how long its work sat in each column.',
  },
  {
    to: '/reports/cumulative-flow',
    title: 'Cumulative flow',
    answers: 'How much work sat in each column over time, and where it is piling up.',
  },
  {
    to: '/reports/changelog',
    title: 'Changelog',
    answers: 'What landed in each day, week or month, at the level a stakeholder reads.',
  },
];

/** The front door to the Reports area: a card per report, each a link in. */
export function ReportsPage() {
  return (
    <div className="hatch-page">
      <PageHeader title="Reports" description="Questions the board can answer about its own history." />

      {REPORTS.map((report) => (
        <Card key={report.to}>
          <Link to={report.to} className="hatch-plan-title">
            {report.title}
          </Link>
          <Text tone="muted">{report.answers}</Text>
        </Card>
      ))}
    </div>
  );
}
