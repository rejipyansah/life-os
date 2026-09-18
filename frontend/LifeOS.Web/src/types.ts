export type AccountType = 'Cash' | 'Bank' | 'EWallet';

export type TransactionType =
  | 'Income'
  | 'Expense'
  | 'Transfer'
  | 'Refund'
  | 'Reversal'
  | 'Adjustment';

export interface AuthMe {
  userId: string;
  userName: string;
  email: string;
  isAuthenticated: boolean;
}

export interface AccountProjection {
  id: string;
  name: string;
  type: number;
  isArchived: boolean;
  balance: number;
  allocated: number;
  available: number;
  createdAt: string;
}

export interface AccountListProjection {
  accounts: AccountProjection[];
  totalBalance: number;
  totalAllocated: number;
  totalAvailable: number;
}

export interface TransactionEntryProjection {
  accountId: string;
  accountName: string;
  amount: number;
}

export interface TransactionProjection {
  id: string;
  type: number;
  amount: number;
  description: string | null;
  categoryName: string | null;
  occurredOn: string;
  createdAt: string;
  relatedTransactionId: string | null;
  feeAmount: number | null;
  entries: TransactionEntryProjection[];
}

export interface CreateAccountCommand {
  name: string;
  type: AccountType;
}

export interface CreateTransactionEntryCommand {
  accountId: string;
  amount: number;
}

export interface CreateTransactionCommand {
  type: TransactionType;
  amount: number;
  description?: string;
  categoryName?: string;
  occurredOn: string;
  relatedTransactionId?: string;
  feeAmount?: number;
  entries: CreateTransactionEntryCommand[];
}

const ACCOUNT_TYPE_NAMES: Record<number, string> = {
  0: 'Cash',
  1: 'Bank',
  2: 'E-Wallet',
};

const TRANSACTION_TYPE_NAMES: Record<number, string> = {
  0: 'Income',
  1: 'Expense',
  2: 'Transfer',
  3: 'Refund',
  4: 'Reversal',
  5: 'Adjustment',
};

export function accountTypeName(type: number): string {
  return ACCOUNT_TYPE_NAMES[type] ?? 'Unknown';
}

export function transactionTypeName(type: number): string {
  return TRANSACTION_TYPE_NAMES[type] ?? 'Unknown';
}
