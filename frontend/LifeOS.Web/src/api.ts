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
import type * as Zod from 'zod';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';
type ApiResponseSchemas = typeof import('./apiSchemas').ApiResponseSchemas;
type ApiResponseSchemaKey = keyof ApiResponseSchemas;
type ApiResponseValue<Key extends ApiResponseSchemaKey> = Zod.infer<ApiResponseSchemas[Key]>;

const loadApiResponseSchemas = () =>
  import('./apiSchemas').then((module) => module.ApiResponseSchemas);

export class ApiRequestError extends Error {
  readonly status: number;
  readonly code: string;
  readonly traceId?: string;

  constructor(message: string, status: number, code: string, traceId?: string) {
    super(message);
    this.name = 'ApiRequestError';
    this.status = status;
    this.code = code;
    this.traceId = traceId;
  }
}

export class GuestSessionExpiredError extends ApiRequestError {
  constructor(traceId?: string) {
    super('Sesi guest telah berakhir atau tidak lagi tersedia.', 410, 'GuestSessionExpired', traceId);
    this.name = 'GuestSessionExpiredError';
  }
}

function errorDetails(body: unknown, status: number): { message: string; code: string; traceId?: string } {
  const response = isRecord(body)
    ? body as { error?: unknown; code?: unknown; traceId?: unknown }
    : undefined;
  const code = typeof response?.code === 'string'
    ? response.code
    : `http_${status}`;
  const serverMessage = typeof response?.error === 'string' ? response.error : undefined;
  const fallbackMessages: Record<number, string> = {
    400: 'Permintaan tidak valid.',
    401: 'Sesi tidak valid. Silakan masuk kembali.',
    403: 'Akses ditolak.',
    404: 'Data yang diminta tidak ditemukan.',
    409: 'Data berubah karena proses lain. Silakan coba lagi.',
    413: 'Data permintaan terlalu besar.',
    422: 'Data belum sesuai ketentuan. Periksa kembali isian.',
    429: 'Terlalu banyak permintaan. Silakan coba lagi nanti.',
    500: 'Terjadi kesalahan pada server. Silakan coba lagi.',
    503: 'Layanan sementara tidak tersedia. Silakan coba lagi nanti.',
  };
  return {
    message: serverMessage ?? fallbackMessages[status] ?? 'Permintaan tidak dapat diproses. Silakan coba lagi.',
    code,
    traceId: typeof response?.traceId === 'string' ? response.traceId : undefined,
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

async function fetchBody(path: string, init?: RequestInit): Promise<{ response: Response; body: unknown }> {
  const response = await fetch(`${API_BASE_URL}${path}`, { ...init, credentials: 'include' });
  return { response, body: await parseBody(response) };
}

function throwApiError(body: unknown, status: number): never {
  throwForExpiredGuestSession(body, status);
  const details = errorDetails(body, status);
  throw new ApiRequestError(details.message, status, details.code, details.traceId);
}

function validateResponse<Key extends ApiResponseSchemaKey>(
  body: unknown,
  path: string,
  schema: ApiResponseSchemas[Key],
): ApiResponseValue<Key> {
  const parsed = schema.safeParse(body);
  if (!parsed.success) {
    throw new ApiRequestError(
      `Respons API untuk ${path} tidak sesuai format yang diharapkan.`,
      502,
      'invalid_api_response',
    );
  }
  return parsed.data as ApiResponseValue<Key>;
}

function throwForExpiredGuestSession(body: unknown, status: number): void {
  if (status !== 410 || !body || typeof body !== 'object'
      || !('code' in body)
      || (body as { code: unknown }).code !== 'GuestSessionExpired') {
    return;
  }

  if (typeof window !== 'undefined')
    window.dispatchEvent(new Event('lifeos:guest-session-expired'));
  const details = errorDetails(body, status);
  throw new GuestSessionExpiredError(details.traceId);
}

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

async function request<Key extends ApiResponseSchemaKey>(
  path: string,
  init: RequestInit | undefined,
  schemaKey: Key,
): Promise<ApiResponseValue<Key>> {
  const [result, schemas] = await Promise.all([fetchBody(path, init), loadApiResponseSchemas()]);
  const { response, body } = result;
  if (!response.ok) throwApiError(body, response.status);
  return validateResponse<Key>(body, path, schemas[schemaKey]);
}

async function requestNoContent(path: string, init?: RequestInit): Promise<void> {
  const { response, body } = await fetchBody(path, init);
  if (!response.ok) throwApiError(body, response.status);
}

const json = (method: string, body?: unknown): RequestInit => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: body === undefined ? undefined : JSON.stringify(body),
});

// ───────────────────────── Auth ─────────────────────────

export async function getAuthMe(): Promise<AuthMe | null> {
  const [result, schemas] = await Promise.all([fetchBody('/api/auth/me'), loadApiResponseSchemas()]);
  const { response, body } = result;
  if (response.status === 401) return null;
  if (!response.ok) throwApiError(body, response.status);
  return validateResponse<'authMe'>(body, '/api/auth/me', schemas.authMe);
}

export async function login(
  email: string,
  password: string
): Promise<{ isAuthenticated: boolean }> {
  return request('/api/auth/login', json('POST', { email, password }), 'login');
}

export async function logout(): Promise<void> {
  await requestNoContent('/api/auth/logout', {
    method: 'POST',
  });
}

export async function createGuestSession(): Promise<{ isGuest: boolean }> {
  return request('/api/guest/session', { method: 'POST' }, 'guestSession');
}

export async function resumeGuestSession(): Promise<{
  isGuest: boolean;
  isNew: boolean;
}> {
  const [result, schemas] = await Promise.all([fetchBody('/api/guest/session'), loadApiResponseSchemas()]);
  const { response, body } = result;
  if (!response.ok) {
    const details = errorDetails(body, response.status);
    if (response.status === 401 && details.code === 'NoGuestSession')
      throw new ApiRequestError('Tidak ada sesi guest yang dapat dilanjutkan.', 401, details.code, details.traceId);
    throwApiError(body, response.status);
  }

  return { ...validateResponse<'guestSession'>(body, '/api/guest/session', schemas.guestSession), isNew: false };
}

// ───────────────────────── Finance state ─────────────────────────

/** Single read model for the Finance page. All figures are derived server-side. */
export async function getFinanceState(): Promise<FinanceStateProjection> {
  return request('/api/finance/state', undefined, 'financeState');
}

// ───────────────────────── Accounts ─────────────────────────

export async function getAccounts(
  includeArchived = false
): Promise<AccountListProjection> {
  const qs = includeArchived ? '?includeArchived=true' : '';
  return request(`/api/finance/accounts${qs}`, undefined, 'accountList');
}

export async function getAccount(id: string): Promise<AccountProjection> {
  return request(`/api/finance/accounts/${id}`, undefined, 'account');
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
  return request('/api/finance/accounts', json('POST', command), 'accountMutation');
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
  return request(`/api/finance/accounts/${id}`, json('PATCH', command), 'accountMutation');
}

// ───────────────────────── Transactions ─────────────────────────

export async function getTransactions(): Promise<{
  items: TransactionProjection[];
}> {
  return request('/api/finance/transactions', undefined, 'transactions');
}

export async function getTransaction(
  id: string
): Promise<TransactionProjection> {
  return request(`/api/finance/transactions/${id}`, undefined, 'transaction');
}

export async function createTransaction(command: CreateTransactionCommand): Promise<{
  transactionId: string;
  type: TransactionType;
  amount: number;
  occurredOn: string;
  createdAt: string;
  entryCount: number;
}> {
  return request('/api/finance/transactions', json('POST', command), 'createTransaction');
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
  return request(`/api/finance/transactions/${id}/reverse`, json('POST', command), 'reverseTransaction');
}

// ───────────────────────── Set-asides ─────────────────────────

export async function getSetAsides(): Promise<{ items: SetAsideProjection[] }> {
  return request('/api/finance/set-asides', undefined, 'setAsides');
}

export async function getSetAside(id: string): Promise<SetAsideProjection> {
  return request(`/api/finance/set-asides/${id}`, undefined, 'setAside');
}

export async function getSetAsideHistory(
  id: string,
  cursor?: string | null,
  pageSize = 30
): Promise<SetAsideHistoryPage> {
  const params = new URLSearchParams({ pageSize: String(pageSize) });
  if (cursor) params.set('cursor', cursor);
  return request(`/api/finance/set-asides/${id}/history?${params.toString()}`, undefined, 'setAsideHistory');
}

export async function createSetAside(
  command: CreateSetAsideCommand
): Promise<SetAsideProjection> {
  return request('/api/finance/set-asides', json('POST', command), 'setAside');
}

export async function updateSetAside(
  id: string,
  command: UpdateSetAsideCommand
): Promise<SetAsideProjection> {
  return request(`/api/finance/set-asides/${id}`, json('PATCH', command), 'setAside');
}

/** Menambah saldo disisihkan (top-up). Bukan Expense.
 *  sourceAccountId: opsional, hanya divalidasi sekali pakai — bukan ikatan pos. */
export async function addToSetAside(
  id: string,
  command: { amount: number; note?: string; sourceAccountId?: string | null }
): Promise<SetAsideOperationResult> {
  return request(`/api/finance/set-asides/${id}/add`, json('POST', command), 'setAsideOperation');
}

/** Menarik kembali saldo disisihkan. Bukan Expense. */
export async function withdrawFromSetAside(
  id: string,
  command: { amount: number; note?: string }
): Promise<SetAsideOperationResult> {
  return request(`/api/finance/set-asides/${id}/withdraw`, json('POST', command), 'setAsideOperation');
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
  return request(`/api/finance/set-asides/${id}/spend`, json('POST', command), 'setAsideOperation');
}

export async function closeSetAside(
  id: string,
  command: {
    reason?: 'Spent' | 'Withdrawn' | 'Cancelled';
    note?: string;
  } = {}
): Promise<SetAsideOperationResult> {
  return request(`/api/finance/set-asides/${id}/close`, json('POST', command), 'setAsideOperation');
}

// ───────────────────────── Upcoming cash events ─────────────────────────

export async function getUpcomingEvents(): Promise<{
  items: UpcomingEventProjection[];
}> {
  return request('/api/finance/upcoming-events', undefined, 'upcomingEvents');
}

export async function getUpcomingEvent(
  id: string
): Promise<UpcomingEventProjection> {
  return request(`/api/finance/upcoming-events/${id}`, undefined, 'upcomingEvent');
}

export async function createUpcomingEvent(
  command: CreateUpcomingEventCommand
): Promise<UpcomingEventProjection> {
  return request('/api/finance/upcoming-events', json('POST', command), 'upcomingEvent');
}

export async function updateUpcomingEvent(
  id: string,
  command: UpdateUpcomingEventCommand
): Promise<UpcomingEventProjection> {
  return request(`/api/finance/upcoming-events/${id}`, json('PATCH', command), 'upcomingEvent');
}

/** Tanpa newDueDate, agenda digeser satu hari. */
export async function postponeUpcomingEvent(
  id: string,
  command: { newDueDate?: string; reason?: string } = {}
): Promise<UpcomingEventProjection> {
  return request(
    `/api/finance/upcoming-events/${id}/postpone`,
    json('POST', command),
    'upcomingEvent',
  );
}

export async function skipUpcomingEvent(
  id: string,
  command: { reason?: string } = {}
): Promise<UpcomingEventProjection> {
  return request(`/api/finance/upcoming-events/${id}/skip`, json('POST', command), 'upcomingEvent');
}

export async function cancelUpcomingEvent(
  id: string,
  command: { reason?: string } = {}
): Promise<UpcomingEventProjection> {
  return request(
    `/api/finance/upcoming-events/${id}/cancel`,
    json('POST', command),
    'upcomingEvent',
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
    json('POST', command),
    'realizeUpcomingEvent',
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
export async function interpret(input: string, accountHint?: string): Promise<InterpretResponse> {
  return request('/api/interpret', json('POST', { input, accountHint }), 'interpret');
}
