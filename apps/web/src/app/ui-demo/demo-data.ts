import type { TransactionType } from '../transactions/transaction-api';

export type CaseStatus = 'Submitted' | 'Under review' | 'Specialist review' | 'Resolved';
export type EscalationStatus = 'Open' | 'Acknowledged' | 'Resolved';

export interface DemoTransaction {
  id: string;
  reference: string;
  description: string;
  type: TransactionType;
  amountMinor: number;
  occurredAt: string;
}

export interface DemoEvent {
  id: string;
  title: string;
  description: string;
  occurredAt: string;
}

export interface PublicCase {
  id: string;
  reference: string;
  transactionId: string;
  merchant: string;
  type: TransactionType;
  amountMinor: number;
  reason: string;
  statement: string;
  caseStatus: CaseStatus;
  decisionStatus: 'Pending' | 'Declined';
  financialStatus: 'Not initiated';
  createdAt: string;
  updatedAt: string;
  timeline: readonly DemoEvent[];
}

export interface AgentCase extends PublicCase {
  customerKey: string;
  customerName: string;
}

export interface DemoNote {
  id: string;
  author: 'Agent' | 'Supervisor';
  text: string;
  occurredAt: string;
}

export interface DemoEscalation {
  id: string;
  reference: string;
  caseId: string;
  reason: string;
  note: string;
  status: EscalationStatus;
  createdAt: string;
  history: readonly DemoEvent[];
}

export function demoId(value: number): string {
  return `10000000-0000-4000-8000-${value.toString().padStart(12, '0')}`;
}

export const DEMO_IDS = {
  transaction: demoId(1),
  availableTransaction: demoId(4),
  timeline: demoId(101),
  investigation: demoId(102),
  otherCustomerCase: demoId(104),
  escalation: demoId(437),
} as const;

export const DEMO_DATE = '2026-11-13T12:00:00Z';
export const DISPUTE_REASONS = [
  "I don't recognise this transaction", 'Duplicate transaction', 'Incorrect amount',
] as const;
export const ESCALATION_REASONS = [
  'Additional investigation required', 'Customer authorization dispute',
  'Mandate mismatch', 'Policy exception review',
] as const;

const merchants: readonly [string, TransactionType, number, string][] = [
  ['Woolworths', 'CardPurchase', 75000, '10'],
  ['Monthly insurance', 'DebitOrder', 42050, '09'],
  ['Account service fee', 'BankCharge', 1500, '08'],
  ['Checkers Sandton', 'CardPurchase', 31620, '07'],
  ['Mobile contract', 'DebitOrder', 59900, '05'],
  ['Streambox subscription', 'CardPurchase', 18900, '04'],
  ['Fresh market', 'CardPurchase', 24150, '04'],
  ['Gym membership', 'DebitOrder', 35000, '04'],
  ['Cash withdrawal fee', 'BankCharge', 1000, '03'],
  ['Coffee corner', 'CardPurchase', 4500, '03'],
  ['Home internet', 'DebitOrder', 69900, '03'],
  ['Monthly account fee', 'BankCharge', 750, '03'],
  ['Bookshop', 'CardPurchase', 22500, '02'],
  ['Parking', 'CardPurchase', 2000, '02'],
  ['Electricity', 'DebitOrder', 45000, '02'],
  ['Statement fee', 'BankCharge', 500, '02'],
  ['Fuel station', 'CardPurchase', 65000, '02'],
  ['Pet supplies', 'CardPurchase', 12900, '02'],
  ['Sports club', 'DebitOrder', 18000, '01'],
  ['Notification fee', 'BankCharge', 200, '01'],
  ['Pharmacy', 'CardPurchase', 9750, '01'],
  ['Bakery', 'CardPurchase', 8500, '01'],
  ['Garden centre', 'CardPurchase', 21000, '01'],
  ['School transport', 'DebitOrder', 60000, '01'],
];

export const DEMO_TRANSACTIONS: readonly DemoTransaction[] = merchants.map(
  ([description, type, amountMinor, day], index) => ({
    id: demoId(index + 1), reference: `TXN-2026-${(index + 123).toString().padStart(6, '0')}`,
    description, type, amountMinor, occurredAt: `2026-11-${day}T08:30:00Z`,
  }),
);

function seedCase(index: number, reference: string, status: CaseStatus, own = true): AgentCase {
  const transaction = DEMO_TRANSACTIONS[index];
  const createdAt = `2026-11-${(10 - index).toString().padStart(2, '0')}T09:14:00Z`;
  const updatedAt = status === 'Specialist review'
    ? (own ? '2026-11-13T10:20:00Z' : '2026-11-12T09:20:00Z')
    : `2026-11-${13 - index}T10:20:00Z`;
  return {
    id: demoId(101 + index), reference, transactionId: own ? transaction.id : demoId(900 + index),
    merchant: transaction.description, type: transaction.type, amountMinor: transaction.amountMinor,
    customerKey: own ? 'demo-customer' : 'other-demo-customer',
    customerName: own ? 'T. Mokone' : 'S. Naidoo',
    reason: DISPUTE_REASONS[0],
    statement: 'I did not make this transaction. Please investigate and confirm the outcome.',
    caseStatus: status, decisionStatus: status === 'Resolved' ? 'Declined' : 'Pending',
    financialStatus: 'Not initiated', createdAt,
    updatedAt,
    timeline: [
      { id: 'submitted', title: 'Dispute submitted', occurredAt: createdAt,
        description: `We received your dispute and created reference ${reference}.` },
      { id: 'review', title: 'Under review', occurredAt: `2026-11-${(11 - index).toString().padStart(2, '0')}T11:30:00Z`,
        description: 'The dispute is being investigated. No refund is confirmed.' },
      ...(status === 'Specialist review' ? [{
        id: 'specialist', title: 'Specialist review', occurredAt: updatedAt,
        description: 'Your case has been referred for additional review.',
      }] : []),
      ...(status === 'Resolved' ? [{
        id: 'resolved', title: 'Final outcome', occurredAt: '2026-11-11T10:20:00Z',
        description: 'The demo review was declined. No financial adjustment was initiated.',
      }] : []),
    ],
  };
}

export function initialCases(): AgentCase[] {
  return [
    seedCase(0, 'CC-2026-01457', 'Specialist review'),
    seedCase(1, 'CC-2026-01421', 'Under review'),
    seedCase(2, 'CC-2026-01388', 'Resolved'),
    seedCase(3, 'CC-2026-01408', 'Specialist review', false),
  ];
}

export function initialEscalations(): DemoEscalation[] {
  return [
    { id: DEMO_IDS.escalation, reference: 'ESC-000437', caseId: DEMO_IDS.timeline,
      reason: ESCALATION_REASONS[0],
      note: 'Standard checks were inconclusive. Specialist review is required before a final decision.',
      status: 'Open', createdAt: '2026-11-13T10:20:00Z',
      history: [{ id: 'created', title: 'Escalation created', occurredAt: '2026-11-13T10:20:00Z',
        description: 'Agent requested specialist review.' }] },
    { id: demoId(421), reference: 'ESC-000421', caseId: DEMO_IDS.otherCustomerCase,
      reason: 'Customer authorization dispute', note: 'Additional authorisation evidence needs review.',
      status: 'Acknowledged', createdAt: '2026-11-12T09:20:00Z',
      history: [
        { id: 'created', title: 'Escalation created', occurredAt: '2026-11-12T09:20:00Z',
          description: 'Agent requested specialist review.' },
        { id: 'acknowledged', title: 'Supervisor acknowledgement', occurredAt: '2026-11-13T09:20:00Z',
          description: 'Supervisor is reviewing this escalation.' },
      ] },
  ];
}
