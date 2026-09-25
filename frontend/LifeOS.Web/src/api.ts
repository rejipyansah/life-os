import type {
  AuthMe,
  AccountType,
  AccountListProjection,
  TransactionType,
  TransactionProjection,
  InterpretResponse,
  AllocationProjection,
  UpdateAllocationCommand,
} from './types';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

async function request<T>(
  path: string,
  init?: RequestInit
): Promise<T> {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    credentials: 'include',
  });

  const body = await res.json();

  if (!res.ok) {
    const msg =
      body &&
      typeof body === 'object' &&
      'error' in body
        ? (body as { error: string }).error
        : `Request failed (${res.status})`;

    throw new Error(msg);
  }

  return body as T;
}

export async function getAuthMe(): Promise<AuthMe | null> {
  const res = await fetch(`${API_BASE_URL}/api/auth/me`, {
    credentials: 'include',
  });

  if (res.status === 401) {
    return null;
  }

  if (!res.ok) {
    throw new Error(`Request failed (${res.status})`);
  }

  return res.json() as Promise<AuthMe>;
}

export async function login(
  email: string,
  password: string
): Promise<{ isAuthenticated: boolean }> {
  return request('/api/auth/login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      email,
      password,
    }),
  });
}

export async function logout(): Promise<void> {
  await fetch(`${API_BASE_URL}/api/auth/logout`, {
    method: 'POST',
    credentials: 'include',
  });
}

export async function createGuestSession(): Promise<{
  isGuest: boolean;
}> {
  return request('/api/guest/session', {
    method: 'POST',
  });
}

export async function resumeGuestSession(): Promise<{
  isGuest: boolean;
  isNew: boolean;
}> {
  const res = await fetch(`${API_BASE_URL}/api/guest/session`, {
    credentials: 'include',
  });

  if (res.ok) {
    return {
      ...(await res.json()),
      isNew: false,
    };
  }

  if (res.status === 401) {
    throw new Error('No guest session');
  }

  const body = await res.json().catch(() => null);

  const msg =
    body &&
    typeof body === 'object' &&
    'error' in body
      ? (body as { error: string }).error
      : `Request failed (${res.status})`;

  throw new Error(msg);
}

export async function getAccounts(
  includeArchived = false
): Promise<AccountListProjection> {
  const qs = includeArchived
    ? '?includeArchived=true'
    : '';

  return request(`/api/finance/accounts${qs}`);
}

export async function createAccount(command: {
  name: string;
  type: AccountType;
}) {
  return request<{
    accountId: string;
    name: string;
    type: AccountType;
    isArchived: boolean;
    createdAt: string;
  }>('/api/finance/accounts', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      name: command.name,
      type: command.type,
    }),
  });
}

export async function updateAccount(
  id: string,
  command: {
    name?: string;
    type?: AccountType;
    isArchived?: boolean;
  }
) {
  return request<{
    accountId: string;
    name: string;
    type: AccountType;
    isArchived: boolean;
    createdAt: string;
  }>(`/api/finance/accounts/${id}`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(command),
  });
}

export async function getTransactions(): Promise<{
  items: TransactionProjection[];
}> {
  return request('/api/finance/transactions');
}

export async function getTransaction(
  id: string
): Promise<TransactionProjection> {
  return request(
    `/api/finance/transactions/${id}`
  );
}

export async function createTransaction(command: {
  type: TransactionType;
  amount: number;
  description?: string;
  categoryName?: string;
  occurredOn: string;
  relatedTransactionId?: string;
  feeAmount?: number;
  entries: {
    accountId: string;
    amount: number;
  }[];
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
    headers: {
      'Content-Type': 'application/json',
    },
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

export async function interpret(
  input: string
): Promise<InterpretResponse> {
  return request<InterpretResponse>(
    '/api/interpret',
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        input,
      }),
    }
  );
}

export async function createAllocation(command: {
  name: string;
  amount: number;
  accountId: string;
}) {
  return request<{
    allocationId: string;
    accountId: string;
    name: string;
    amount: number;
    status: string;
    createdAt: string;
  }>('/api/finance/allocations', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      name: command.name,
      amount: command.amount,
      accountId: command.accountId,
    }),
  });
}

export async function getAllocations(): Promise<
  AllocationProjection[]
> {
  return request<AllocationProjection[]>(
    '/api/finance/allocations'
  );
}

export async function updateAllocation(
  id: string,
  command: UpdateAllocationCommand
) {
  return request<{
    allocationId: string;
    accountId: string;
    name: string;
    amount: number;
    status: string;
    createdAt: string;
  }>(`/api/finance/allocations/${id}`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(command),
  });
}