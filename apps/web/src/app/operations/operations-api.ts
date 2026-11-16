import { InjectionToken } from '@angular/core';
import { AgentDisputesApi } from '../agent/agent-disputes-api';
import { SupervisorEscalationsApi } from '../supervisor/supervisor-escalations-api';

export interface OperationsApi {
  agent: AgentDisputesApi;
  supervisor: SupervisorEscalationsApi;
}

export const OPERATIONS_API = new InjectionToken<OperationsApi>('OPERATIONS_API');
