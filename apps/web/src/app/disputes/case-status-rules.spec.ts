import { isCaseClosed, isCaseOpen } from './case-status-rules';
import { DisputeCaseStatus } from './dispute-api';

describe('Case status rules', () => {
  it('treats a rejected but not closed case as still open for follow-up', () => {
    const statuses: DisputeCaseStatus[] = [
      'Submitted', 'ManualReview', 'UnderReview', 'MandateVerified',
      'ReversalRequested', 'ReversalConfirmed', 'Rejected',
    ];
    expect(statuses.every(status => isCaseOpen(status) && !isCaseClosed(status))).toBe(true);
    expect(isCaseOpen('Closed')).toBe(false);
    expect(isCaseClosed('Closed')).toBe(true);
  });
});
