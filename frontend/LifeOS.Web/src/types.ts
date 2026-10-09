// API contract types — matches the Life OS Finance backend domain.
// These describe the wire format only. Domain calculations come from the backend
// read model (`FinanceStateProjection`); the client must not recompute financial state.

export type AccountType = 'Cash' | 'Bank' | 'EWallet' | 'Credit';

export type TransactionType =
  | 'Income'
  | 'Expense'
  | 'Transfer'
  | 'Refund'
  | 'Reversal'
  | 'Adjustment';

export type SetAsideKind =
  | 'Saving'
  | 'RoutineIncremental'
  | 'RoutineBatch'
  | 'SingleSpend';

export type SetAsideCycleKind =
  | 'None'
  | 'Weekly'
  | 'Monthly'
  | 'Quarterly'
  | 'SemiAnnual'
  | 'Annual';

export type SetAsideEntryType =
  | 'Opened'
  | 'Added'
  | 'Withdrawn'
  | 'Spent'
  | 'CycleFunding'
  | 'Released'
  | 'Closed';

export type SetAsideStatus = 'Active' | 'Closed';

export type SetAsideCloseReason = 'Spent' | 'Withdrawn' | 'Cancelled';

export type UpcomingEventDirection = 'Income' | 'Expense';

export type UpcomingEventStatus =
  | 'Scheduled'
  | 'Realized'
  | 'Skipped'
  | 'Cancelled';

export type UpcomingEventScheduleKind = 'Scheduled' | 'Flexible';

export type UpcomingEventRecurrence =
  | 'None'
  | 'Weekly'
  | 'Monthly'
  | 'Quarterly'
  | 'Annual';

export interface AuthMe {
  userId: string;
  userName: string;
  email: string;
  isAuthenticated: boolean;
}

// ───────────────────────── Account ─────────────────────────

export interface AccountProjection {
  id: string;
  name: string;
  type: AccountType;
  isArchived: boolean;
  /** Saldo riil = SUM(TransactionEntry.Amount). */
  actualBalance: number;
  /** Uang yang sedang disisihkan pada akun ini. */
  setAsideAmount: number;
  /** Saldo tersedia = actualBalance - setAsideAmount. */
  availableBalance: number;
  createdAt: string;
}

export interface AccountListProjection {
  accounts: AccountProjection[];
  totalActualBalance: number;
  totalSetAsideAmount: number;
  totalAvailableBalance: number;
}

export interface AccountStateProjection {
  id: string;
  name: string;
  type: AccountType;
  isArchived: boolean;
  /** Saldo riil = SUM(TransactionEntry.Amount). Lokasi uang di akun ini. */
  actualBalance: number;
  /**
   * SELALU 0 — alokasi (Dana yang Disisihkan) scope-wide, bukan milik akun tertentu.
   * SetAside.AccountId legacy tidak dipakai untuk mengurangi saldo per akun.
   */
  setAsideAmount: number;
  /** Saldo aktual akun ini. Alokasi dihitung di totalAvailable (scope-wide). */
  availableBalance: number;
  createdAt: string;
}

export interface CreateAccountCommand {
  name: string;
  type: AccountType;
}

export interface UpdateAccountCommand {
  name?: string;
  type?: AccountType;
  isArchived?: boolean;
}

// ───────────────────────── Transaction ─────────────────────────

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
  isReversed: boolean;
  reversalReason: string | null;
  /** Opsional. Dana yang Disisihkan (pos) yang dialokasikan/dilepas. */
  setAsideId: string | null;
  /** Nama pos alokasi bila ada. */
  setAsideName: string | null;
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
  /**
   * Opsional. Dana yang Disisihkan (pos) yang dialokasikan/dilepas.
   * INDEPENDEN dari Entries[].AccountId (Sumber Dana):
   *   entries[].accountId = Sumber Dana (uang keluar/masuk dari mana)
   *   setAsideId          = alokasi/tujuan uang yang digunakan
   */
  setAsideId?: string | null;
}

export interface ReverseTransactionCommand {
  reason?: string;
  occurredOn?: string;
}

// ───────────────────────── Set-aside ─────────────────────────

export interface SetAsideEntryProjection {
  id: string;
  type: SetAsideEntryType;
  amount: number;
  transactionId: string | null;
  note: string | null;
  transactionCategory?: string | null;
  createdAt: string;
  balanceAfter: number;
  transaction: SetAsideTransactionSummary | null;
}

export interface SetAsideTransactionSummary {
  id: string;
  type: TransactionType;
  amount: number;
  description: string | null;
  categoryName: string | null;
  occurredOn: string;
  relatedDescription: string | null;
}

export interface SetAsideHistoryPage {
  items: SetAsideEntryProjection[];
  hasMore: boolean;
  nextCursor: string | null;
}

export interface SetAsideProjection {
  id: string;
  /**
   * LEGACY ONLY — alokasi tidak terikat Sumber Dana.
   * Nilai legacy dibiarkan untuk kompatibilitas data lama; tidak dipakai perhitungan.
   */
  accountId: string | null;
  /** LEGACY ONLY. */
  accountName: string | null;
  /**
   * Hint non-binding: sumber dana default untuk proses manual (top-up/pakai).
   * Hanya pre-select UI — bukan ikatan, bukan validasi.
   */
  defaultSourceAccountId: string | null;
  /** Nama sumber dana default untuk tampilan. */
  defaultSourceAccountName: string | null;
  name: string;
  kind: SetAsideKind | null;
  note: string | null;
  transactionCategory?: string | null;
  /** Saldo yang sedang disisihkan, diturunkan dari history. */
  amount: number;
  targetAmount: number | null;
  /** Jarak ke target saat ini: max(0, target - amount). */
  targetShortfall: number;
  cycleKind: SetAsideCycleKind;
  cycleAnchorDate: string;
  currentCycleStart: string | null;
  currentCycleEnd: string | null;
  isCycleRolloverPending: boolean;
  cycleFundingRequired: number;
  cycleSurplus: number;
  cycleFundingShortfall: number;
  isUnderfunded: boolean;
  isCycleExecuted: boolean;
  usedAmount: number;
  status: SetAsideStatus;
  closeReason: SetAsideCloseReason | null;
  createdAt: string;
  updatedAt: string;
  recentEntries: SetAsideEntryProjection[];
}

export interface CreateSetAsideCommand {
  /**
   * Opsional. Sumber dana untuk validasi pendanaan awal saja (bila amount > 0).
   * TIDAK disimpan permanen di SetAside — pos tidak pernah terikat rekening.
   */
  sourceAccountId?: string | null;
  name: string;
  kind?: SetAsideKind;
  note?: string;
  transactionCategory?: string;
  targetAmount?: number | null;
  cycleKind?: SetAsideCycleKind;
  /** Saldo yang langsung disisihkan saat dibuat. Boleh 0. */
  amount?: number;
}

export interface UpdateSetAsideCommand {
  name?: string;
  note?: string;
  transactionCategory?: string;
  targetAmount?: number;
  removeTarget?: boolean;
  cycleKind?: SetAsideCycleKind;
}

export interface SetAsideOperationResult {
  setAside: SetAsideProjection;
  entry: SetAsideEntryProjection;
  /** Transaksi Expense yang tercipta; hanya untuk operasi spend. */
  transactionId: string | null;
  cycleFundingShortfall: number;
}

// ───────────────────────── Upcoming cash events ─────────────────────────

export interface UpcomingEventProjection {
  id: string;
  /**
   * LEGACY ONLY — Rencana baru tidak pernah mengisi akun.
   * Akun hanya dipilih saat realizasi. Nilai legacy hanya untuk kompatibilitas.
   */
  accountId: string | null;
  /** LEGACY ONLY. */
  accountName: string | null;
  title: string;
  amount: number;
  direction: UpcomingEventDirection;
  categoryName: string | null;
  note: string | null;
  dueDate: string | null;
  scheduleKind: UpcomingEventScheduleKind;
  recurrence: UpcomingEventRecurrence;
  status: UpcomingEventStatus;
  realizedTransactionId: string | null;
  statusReason: string | null;
  isDue: boolean;
  isOverdue: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateUpcomingEventCommand {
  /**
   * Tidak dipakai untuk create — Rencana tidak terikat Sumber Dana.
   * Akun hanya dipilih saat realizasi.
   */
  accountId?: string | null;
  title: string;
  amount: number;
  direction: UpcomingEventDirection;
  categoryName?: string;
  note?: string;
  dueDate?: string | null;
  scheduleKind?: UpcomingEventScheduleKind;
  recurrence?: UpcomingEventRecurrence;
}

export interface UpdateUpcomingEventCommand {
  title?: string;
  amount?: number;
  direction?: UpcomingEventDirection;
  categoryName?: string;
  note?: string;
  dueDate?: string | null;
  clearDueDate?: boolean;
  scheduleKind?: UpcomingEventScheduleKind;
  recurrence?: UpcomingEventRecurrence;
}

/**
 * Realisasi Rencana menjadi transaksi nyata.
 * accountId = Sumber Dana tempat uang keluar/masuk — WAJIB dipilih saat realizasi.
 * setAsideId = Dana yang Disisihkan opsional yang dialokasikan/dilepas.
 */
export interface RealizeUpcomingEventCommand {
  accountId: string;
  setAsideId?: string | null;
  occurredOn?: string;
  description?: string;
}

export interface RealizeUpcomingEventResult {
  event: UpcomingEventProjection;
  transactionId: string;
}

// ───────────────────────── Finance state (read model) ─────────────────────────

/**
 * DUA ANGKA TERPISAH — jangan disamakan:
 *   totalAvailable = totalActualBalance − totalSetAside
 *     Uang yang belum dialokasikan ke Dana yang Disisihkan (sebelum komitmen Rencana).
 *   freeCash (Uang Bebas) = totalActualBalance − totalCommittedSetAside
 *                           − scheduledExpenseCommitments
 *     Uang yang benar-benar bebas dibelanjakan setelah semua komitmen.
 */
export interface FinanceStateProjection {
  today: string;

  totalActualBalance: number;
  totalSetAside: number;
  pendingCycleFunding: number;
  pendingCycleSurplus: number;
  totalCommittedSetAside: number;

  /**
   * TotalAvailable = totalActualBalance − totalSetAside.
   * Uang yang belum dialokasikan ke Dana yang Disisihkan.
   * BERBEDA dari freeCash (yang juga mengurangi komitmen Rencana).
   */
  totalAvailable: number;

  dueObligations: number;
  dueObligationsCount: number;
  overdueObligationsCount: number;

  /**
   * Komitmen SEMUA agenda pengeluaran terjadwal (sekali jalan & ber-siklus).
   * Mengurangi freeCash sejak agenda dibuat, tanpa menjadi transaksi.
   */
  scheduledExpenseCommitments: number;

  /**
   * FreeCash (Uang Bebas) — DERIVED STATE.
   *   = totalActualBalance - totalCommittedSetAside - scheduledExpenseCommitments
   * Tidak pernah di-clamp ke 0. BERBEDA dari totalAvailable.
   */
  freeCash: number;

  hasUnpaidBills: boolean;
  allBillsPaid: boolean;

  accounts: AccountStateProjection[];
  setAsides: SetAsideProjection[];
  upcomingEvents: UpcomingEventProjection[];
  dueEvents: UpcomingEventProjection[];
  recentTransactions: TransactionProjection[];
}

// ───────────────────────── Natural input ─────────────────────────

export interface InterpretTransactionData {
  type: string;
  amount: number;
  description: string | null;
  account: string | null;
  toAccount: string | null;
  date: string | null;
  feeAmount: number | null;
}

export interface InterpretSetAsideData {
  name: string;
  amount: number;
  account: string | null;
}

export interface InterpretEventData {
  title: string;
  amount: number;
  direction: string;
  account: string | null;
  date: string | null;
}

export interface InterpretResponse {
  intent: string;
  state: 'Ready' | 'NeedsClarification' | 'Unsupported';
  preview: InterpretTransactionData | null;
  command: CreateTransactionCommand | null;
  setAsidePreview: InterpretSetAsideData | null;
  setAsideCommand: CreateSetAsideCommand | null;
  eventPreview: InterpretEventData | null;
  eventCommand: CreateUpcomingEventCommand | null;
  clarifications: string[];
}
