import { HttpErrorResponse } from '@angular/common/http';
import { problemCode } from '../disputes/dispute-api';

export function assignmentFailureReason(error: unknown): string {
  const code = problemCode(error);
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  if (code === 'dispute.alreadyAssigned' || status === 409) return 'already assigned to another employee';
  if (code === 'dispute.assignmentForbidden' || status === 403) return 'only the assignee or a supervisor can release it';
  if (code === 'dispute.notFound' || status === 404) return 'the case could not be found';
  return 'the assignment service is unavailable';
}
