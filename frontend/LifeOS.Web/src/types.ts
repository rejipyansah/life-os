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
  actualBalance: number;
  setAsideAmount: number;
  availableBalance: number;
  pendingCycleFunding: number;
  pendingCycleSurplus: number;
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
  createdAt: string;
}

export interface SetAsideProjection {
  id: string;
  accountId: string;
  accountName: string;
  name: string;
  kind: SetAsideKind | null;
  note: string | null;
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
  usedAmount: number;
  status: SetAsideStatus;
  closeReason: SetAsideCloseReason | null;
  createdAt: string;
  updatedAt: string;
  recentEntries: SetAsideEntryProjection[];
}

export interface CreateSetAsideCommand {
  accountId: string;
  name: string;
  kind?: SetAsideKind;
  note?: string;
  targetAmount?: number | null;
  cycleKind?: SetAsideCycleKind;
  /** Saldo yang langsung disisihkan saat dibuat. Boleh 0. */
  amount?: number;
}

export interface UpdateSetAsideCommand {
  name?: string;
  note?: string;
  targetAmount?: number;
  removeTarget?: boolean;
  cycleKind?: SetAsideCycleKind;
  accountId?: string;
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
  accountId: string | null;
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
  accountId?: string | null;
  clearAccount?: boolean;
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

export interface RealizeUpcomingEventResult {
  event: UpcomingEventProjection;
  transactionId: string;
}

// ───────────────────────── Finance state (read model) ─────────────────────────

export interface FinanceStateProjection {
  today: string;

  totalActualBalance: number;
  totalSetAside: number;
  pendingCycleFunding: number;
  pendingCycleSurplus: number;
  totalCommittedSetAside: number;
  totalAvailable: number;

  dueObligations: number;
  dueObligationsCount: number;
  overdueObligationsCount: number;

  /**
   * Komitmen SEMUA agenda pengeluaran terjadwal (sekali jalan & ber-siklus).
   * Mengurangi Uang Bebas sejak agenda dibuat, tanpa menjadi transaksi
   * (mirip disisihkan).
   */
  scheduledExpenseCommitments: number;

  /**
   * Uang Bebas — DERIVED STATE.
   *   = totalActualBalance - totalCommittedSetAside - scheduledExpenseCommitments
   * Tidak pernah di-clamp ke 0.
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
