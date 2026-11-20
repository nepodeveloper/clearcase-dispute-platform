import { AgentCaseStatus, AgentDisputeCategory, AssignmentFilter } from './agent-disputes-api';

export const queueFilterStorageKey = 'clearcase.queue.filters';
export const maxQueueFilterPresets = 10;
export const maxPresetNameLength = 40;

/** Non-sensitive view preferences only: never case data, identities or tokens. */
export interface QueueFilterPreset {
  name: string;
  search: string;
  category: AgentDisputeCategory | '';
  status: AgentCaseStatus | '';
  assignment: AssignmentFilter;
}

const categories: readonly string[] = ['', 'CardPurchase', 'DebitOrder', 'BankCharge'];
const statuses: readonly string[] = ['', 'Submitted', 'ManualReview', 'UnderReview', 'MandateVerified',
  'ReversalRequested', 'ReversalConfirmed', 'Closed', 'Rejected'];
const assignments: readonly string[] = ['all', 'mine', 'unassigned'];

function isPreset(value: unknown): value is QueueFilterPreset {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Record<string, unknown>;
  return typeof candidate['name'] === 'string' && candidate['name'].trim() !== ''
    && candidate['name'].length <= maxPresetNameLength
    && typeof candidate['search'] === 'string' && candidate['search'].length <= 100
    && typeof candidate['category'] === 'string' && categories.includes(candidate['category'])
    && typeof candidate['status'] === 'string' && statuses.includes(candidate['status'])
    && typeof candidate['assignment'] === 'string' && assignments.includes(candidate['assignment']);
}

function storage(): Storage | null {
  try {
    return globalThis.localStorage ?? null;
  } catch {
    return null;
  }
}

/** Reads saved presets, dropping anything that does not have the expected shape. */
export function readQueueFilterPresets(): QueueFilterPreset[] {
  try {
    const raw = storage()?.getItem(queueFilterStorageKey);
    if (raw === null || raw === undefined) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    const valid: unknown[] = parsed;
    return valid.filter(isPreset).slice(0, maxQueueFilterPresets)
      .map(({ name, search, category, status, assignment }) => ({ name, search, category, status, assignment }));
  } catch {
    return [];
  }
}

/** Returns false when storage is unavailable so the caller can say the preset is not saved. */
export function writeQueueFilterPresets(presets: readonly QueueFilterPreset[]): boolean {
  try {
    const target = storage();
    if (target === null) return false;
    target.setItem(queueFilterStorageKey, JSON.stringify(presets));
    return true;
  } catch {
    return false;
  }
}

/** Replaces a preset with the same name (case-insensitive), otherwise appends; null when the limit is reached. */
export function upsertPreset(presets: readonly QueueFilterPreset[], preset: QueueFilterPreset): QueueFilterPreset[] | null {
  const existing = presets.findIndex(item => item.name.toLowerCase() === preset.name.toLowerCase());
  if (existing >= 0) return presets.map((item, index) => index === existing ? preset : item);
  return presets.length >= maxQueueFilterPresets ? null : [...presets, preset];
}
