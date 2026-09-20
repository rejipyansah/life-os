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

export interface InterpretTransactionData {
  type: string;
  amount: number;
  description: string | null;
  account: string | null;
  toAccount: string | null;
  date: string | null;
  feeAmount: number | null;
}

export interface InterpretAllocationData {
  name: string;
  amount: number;
  account: string | null;
}

export interface CreateAllocationCommand {
  name: string;
  amount: number;
  accountId: string;
}

export interface InterpretResponse {
  intent: string;
  state: 'Ready' | 'NeedsClarification' | 'Unsupported';
  preview: InterpretTransactionData | null;
  command: CreateTransactionCommand | null;
  allocationPreview: InterpretAllocationData | null;
  allocationCommand: CreateAllocationCommand | null;
  clarifications: string[];
}

export interface AllocationProjection {
  allocationId: string;
  accountId: string;
  name: string;
  amount: number;
  isActive: boolean;
  createdAt: string;
}

export interface UpdateAllocationCommand {
  name?: string;
  amount?: number;
  accountId?: string;
  isActive?: boolean;
}
