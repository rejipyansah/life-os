import type {
  AuthMe,
  AccountType,
  AccountListProjection,
  TransactionType,
  TransactionProjection,
} from './types';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, init);
  const body = await res.json();
  if (!res.ok) {
    const msg = (body && typeof body === 'object' && 'error' in body)
      ? (body as { error: string }).error
      : `Request failed (${res.status})`;
    throw new Error(msg);
  }
  return body as T;
}

export async function getAuthMe(): Promise<AuthMe> {
  return request<AuthMe>('/api/auth/me');
}

export async function login(email: string, password: string): Promise<{ isAuthenticated: boolean }> {
  return request('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
}

export async function logout(): Promise<void> {
  await fetch('/api/auth/logout', { method: 'POST' });
}

export async function createGuestSession(): Promise<{ isGuest: boolean }> {
  return request('/api/guest/session', { method: 'POST' });
}

export async function getAccounts(includeArchived = false): Promise<AccountListProjection> {
  const qs = includeArchived ? '?includeArchived=true' : '';
  return request(`/api/finance/accounts${qs}`);
}

export async function createAccount(command: { name: string; type: AccountType }) {
  return request<{
    accountId: string;
    name: string;
    type: AccountType;
    isArchived: boolean;
    createdAt: string;
  }>('/api/finance/accounts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name: command.name, type: command.type }),
  });
}

export async function getTransactions(): Promise<{ items: TransactionProjection[] }> {
  return request('/api/finance/transactions');
}

export async function createTransaction(command: {
  type: TransactionType;
  amount: number;
  description?: string;
  categoryName?: string;
  occurredOn: string;
  relatedTransactionId?: string;
  feeAmount?: number;
  entries: { accountId: string; amount: number }[];
}) {
  return request<{
    transactionId: string;
    type: TransactionType;
    amount: number;
    occurredOn: string;
    createdAt: string;
    entryCount: number;
  }>('/api/finance/transactions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      type: command.type,
      amount: command.amount,
      description: command.description,
      categoryName: command.categoryName,
      occurredOn: command.occurredOn,
      relatedTransactionId: command.relatedTransactionId,
      feeAmount: command.feeAmount,
      entries: command.entries,
    }),
  });
}
