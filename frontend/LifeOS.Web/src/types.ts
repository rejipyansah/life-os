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
  type: AccountType;
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
  type: TransactionType;
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
