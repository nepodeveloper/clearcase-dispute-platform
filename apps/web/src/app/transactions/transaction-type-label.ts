import { TransactionType } from './transaction-api';

export function transactionTypeLabel(type: TransactionType): string {
  return { CardPurchase: 'Card purchase', DebitOrder: 'Debit order', BankCharge: 'Bank charge' }[type];
}
