import type { DisputeCaseStatus } from './dispute-api';

// Shared UI case completion semantics. Approval/rejection alone does not close a case.
export function isCaseClosed(status: DisputeCaseStatus): boolean {
  return status === 'Closed';
}
export function isCaseOpen(status: DisputeCaseStatus): boolean {
  return !isCaseClosed(status);
}
