import { AgentAvailableAction } from './agent-disputes-api';

/**
 * Presentation model for a server-provided command, never an authorization or business-policy decision.
 * The API decides which commands are available and re-evaluates eligibility, bank-employee scope and expectedVersion.
 */
export interface StaffAction {
  command: string;
  label: string;
  description: string;
  confirmsMoney: boolean;
  terminal: boolean;
}

export function toStaffAction(action: AgentAvailableAction): StaffAction {
  return {
    command: action.command,
    label: action.label,
    description: action.description,
    confirmsMoney: action.movesMoney,
    terminal: action.terminal,
  };
}
