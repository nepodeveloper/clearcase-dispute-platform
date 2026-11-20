import { AgentAvailableAction } from './agent-disputes-api';

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
