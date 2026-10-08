import type {
  AuthMe,
  AccountType,
  AccountListProjection,
  AccountProjection,
  TransactionType,
  TransactionProjection,
  CreateTransactionCommand,
  ReverseTransactionCommand,
  SetAsideProjection,
  SetAsideHistoryPage,
  SetAsideOperationResult,
  CreateSetAsideCommand,
  UpdateSetAsideCommand,
  UpcomingEventProjection,
  CreateUpcomingEventCommand,
  UpdateUpcomingEventCommand,
  RealizeUpcomingEventResult,
  FinanceStateProjection,
  InterpretResponse,
} from './types';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

/**
 * Parse body respons dengan aman. Backend bisa mengembalikan HTML
 * (halaman error ASP.NET) atau teks non-JSON saat server gagal —
 * jangan pernah melempar SyntaxError mentah ke UI.
 */
async function parseBody(res: Response): Promise<unknown> {
  const text = await res.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function errorMessageFromBody(body: unknown, status: number): string {
  if (body && typeof body === 'object' && 'error' in body) {
    const msg = (body as { error: unknown }).error;
    if (typeof msg === 'string' && msg) return msg;
  }
  return `Request failed (${status})`;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    credentials: 'include',
  });

  const body = await parseBody(res);

  if (!res.ok) {
    throw new Error(errorMessageFromBody(body, res.status));
  }

  return body as T;
}

async function requestNoContent(path: string, init?: RequestInit): Promise<void> {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    credentials: 'include',
  });

  if (res.ok) return;

  const body = await parseBody(res);
  throw new Error(errorMessageFromBody(body, res.status));
}

const json = (method: string, body?: unknown): RequestInit => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: body === undefined ? undefined : JSON.stringify(body),
});

// ───────────────────────── Auth ─────────────────────────

export async function getAuthMe(): Promise<AuthMe | null> {
  const res = await fetch(`${API_BASE_URL}/api/auth/me`, {
    credentials: 'include',
  });

  if (res.status === 401) return null;
  if (!res.ok) throw new Error(`Request failed (${res.status})`);

  return res.json() as Promise<AuthMe>;
}

export async function login(
  email: string,
  password: string
): Promise<{ isAuthenticated: boolean }> {
  return request('/api/auth/login', json('POST', { email, password }));
}

export async function logout(): Promise<void> {
  await fetch(`${API_BASE_URL}/api/auth/logout`, {
    method: 'POST',
    credentials: 'include',
  });
}

export async function createGuestSession(): Promise<{ isGuest: boolean }> {
  return request('/api/guest/session', { method: 'POST' });
}

export async function resumeGuestSession(): Promise<{
  isGuest: boolean;
  isNew: boolean;
}> {
  const res = await fetch(`${API_BASE_URL}/api/guest/session`, {
    credentials: 'include',
  });

  if (res.ok) {
    return { ...(await res.json()), isNew: false };
  }

  if (res.status === 401) throw new Error('No guest session');

  const body = await res.json().catch(() => null);
  const msg =
    body && typeof body === 'object' && 'error' in body
      ? (body as { error: string }).error
      : `Request failed (${res.status})`;

  throw new Error(msg);
}

// ───────────────────────── Finance state ─────────────────────────

/** Single read model for the Finance page. All figures are derived server-side. */
export async function getFinanceState(): Promise<FinanceStateProjection> {
  return request('/api/finance/state');
}

// ───────────────────────── Accounts ─────────────────────────

export async function getAccounts(
  includeArchived = false
): Promise<AccountListProjection> {
  const qs = includeArchived ? '?includeArchived=true' : '';
  return request(`/api/finance/accounts${qs}`);
}

export async function getAccount(id: string): Promise<AccountProjection> {
  return request(`/api/finance/accounts/${id}`);
}

export async function createAccount(command: {
  name: string;
  type: AccountType;
}): Promise<{
  accountId: string;
  name: string;
  type: AccountType;
  isArchived: boolean;
  createdAt: string;
}> {
  return request('/api/finance/accounts', json('POST', command));
}

export async function updateAccount(
  id: string,
  command: { name?: string; type?: AccountType; isArchived?: boolean }
): Promise<{
  accountId: string;
  name: string;
  type: AccountType;
  isArchived: boolean;
  createdAt: string;
}> {
  return request(`/api/finance/accounts/${id}`, json('PATCH', command));
}

// ───────────────────────── Transactions ─────────────────────────

export async function getTransactions(): Promise<{
  items: TransactionProjection[];
}> {
  return request('/api/finance/transactions');
}

export async function getTransaction(
  id: string
): Promise<TransactionProjection> {
  return request(`/api/finance/transactions/${id}`);
}

export async function createTransaction(command: CreateTransactionCommand): Promise<{
  transactionId: string;
  type: TransactionType;
  amount: number;
  occurredOn: string;
  createdAt: string;
  entryCount: number;
}> {
  return request('/api/finance/transactions', json('POST', command));
}

/**
 * Membatalkan transaksi tanpa mengubah histori: membuat Reversal berlawanan arah
 * yang dihubungkan ke transaksi asli.
 */
export async function reverseTransaction(
  id: string,
  command: ReverseTransactionCommand = {}
): Promise<{
  transactionId: string;
  type: TransactionType;
  amount: number;
  relatedTransactionId: string | null;
  occurredOn: string;
  createdAt: string;
}> {
  return request(`/api/finance/transactions/${id}/reverse`, json('POST', command));
}

// ───────────────────────── Set-asides ─────────────────────────

export async function getSetAsides(): Promise<{ items: SetAsideProjection[] }> {
  return request('/api/finance/set-asides');
}

export async function getSetAside(id: string): Promise<SetAsideProjection> {
  return request(`/api/finance/set-asides/${id}`);
}

export async function getSetAsideHistory(
  id: string,
  cursor?: string | null,
  pageSize = 30
): Promise<SetAsideHistoryPage> {
  const params = new URLSearchParams({ pageSize: String(pageSize) });
  if (cursor) params.set('cursor', cursor);
  return request(`/api/finance/set-asides/${id}/history?${params.toString()}`);
}

export async function createSetAside(
  command: CreateSetAsideCommand
): Promise<SetAsideProjection> {
  return request('/api/finance/set-asides', json('POST', command));
}

export async function updateSetAside(
  id: string,
  command: UpdateSetAsideCommand
): Promise<SetAsideProjection> {
  return request(`/api/finance/set-asides/${id}`, json('PATCH', command));
}

/** Menambah saldo disisihkan (top-up). Bukan Expense.
 *  sourceAccountId: opsional, hanya divalidasi sekali pakai — bukan ikatan pos. */
export async function addToSetAside(
  id: string,
  command: { amount: number; note?: string; sourceAccountId?: string | null }
): Promise<SetAsideOperationResult> {
  return request(`/api/finance/set-asides/${id}/add`, json('POST', command));
}

/** Menarik kembali saldo disisihkan. Bukan Expense. */
export async function withdrawFromSetAside(
  id: string,
  command: { amount: number; note?: string }
): Promise<SetAsideOperationResult> {
  return request(`/api/finance/set-asides/${id}/withdraw`, json('POST', command));
}

/**
 * Pengeluaran riil dari set-aside: membuat Expense sekaligus melepas reservasi.
 * sourceAccountId = Sumber Dana tempat uang BENAR-BENAR keluar. WAJIB.
 * Seluruh nominal keluar dari akun itu; pos melepas min(amount, saldoPos).
 */
export async function spendFromSetAside(
  id: string,
  command: {
    sourceAccountId: string;
    amount: number;
    description?: string;
    categoryName?: string;
    occurredOn: string;
    note?: string;
  }
): Promise<SetAsideOperationResult> {
  return request(`/api/finance/set-asides/${id}/spend`, json('POST', command));
}

export async function closeSetAside(
  id: string,
  command: {
    reason?: 'Spent' | 'Withdrawn' | 'Cancelled';
    note?: string;
  } = {}
): Promise<SetAsideOperationResult> {
  return request(`/api/finance/set-asides/${id}/close`, json('POST', command));
}

// ───────────────────────── Upcoming cash events ─────────────────────────

export async function getUpcomingEvents(): Promise<{
  items: UpcomingEventProjection[];
}> {
  return request('/api/finance/upcoming-events');
}

export async function getUpcomingEvent(
  id: string
): Promise<UpcomingEventProjection> {
  return request(`/api/finance/upcoming-events/${id}`);
}

export async function createUpcomingEvent(
  command: CreateUpcomingEventCommand
): Promise<UpcomingEventProjection> {
  return request('/api/finance/upcoming-events', json('POST', command));
}

export async function updateUpcomingEvent(
  id: string,
  command: UpdateUpcomingEventCommand
): Promise<UpcomingEventProjection> {
  return request(`/api/finance/upcoming-events/${id}`, json('PATCH', command));
}

/** Tanpa newDueDate, agenda digeser satu hari. */
export async function postponeUpcomingEvent(
  id: string,
  command: { newDueDate?: string; reason?: string } = {}
): Promise<UpcomingEventProjection> {
  return request(
    `/api/finance/upcoming-events/${id}/postpone`,
    json('POST', command)
  );
}

export async function skipUpcomingEvent(
  id: string,
  command: { reason?: string } = {}
): Promise<UpcomingEventProjection> {
  return request(`/api/finance/upcoming-events/${id}/skip`, json('POST', command));
}

export async function cancelUpcomingEvent(
  id: string,
  command: { reason?: string } = {}
): Promise<UpcomingEventProjection> {
  return request(
    `/api/finance/upcoming-events/${id}/cancel`,
    json('POST', command)
  );
}

/**
 * Merealisasikan agenda menjadi transaksi riil.
 * accountId = Sumber Dana tempat uang keluar/masuk — WAJIB dipilih saat realizasi.
 * setAsideId = Dana yang Disisihkan opsional yang dialokasikan/dilepas.
 */
export async function realizeUpcomingEvent(
  id: string,
  command: { accountId: string; setAsideId?: string | null; occurredOn?: string; description?: string }
): Promise<RealizeUpcomingEventResult> {
  return request(
    `/api/finance/upcoming-events/${id}/realize`,
    json('POST', command)
  );
}

export async function deleteUpcomingEvent(id: string): Promise<void> {
  await requestNoContent(`/api/finance/upcoming-events/${id}`, {
    method: 'DELETE',
  });
}

// ───────────────────────── Natural input ─────────────────────────

/**
 * Interpreter only: menghasilkan structured command/preview tanpa menyentuh database.
 * User mengonfirmasi, lalu command dikirim ke endpoint yang sesuai.
 */
export async function interpret(input: string): Promise<InterpretResponse> {
  return request('/api/interpret', json('POST', { input }));
}
