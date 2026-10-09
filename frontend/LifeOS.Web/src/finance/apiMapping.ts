/**
 * API → UI mapping layer.
 *
 * The Finance FINAL UI keeps its own view-model types (Account, PosItem, AgendaItem,
 * BillDue, Transaction, FinanceDerived). The backend owns the financial state and
 * returns `FinanceStateProjection`; this module translates that projection into the
 * shapes the existing components already consume.
 *
 * Nothing here computes financial state. Every amount comes straight from the backend
 * read model — `freeCash`, balances and set-aside figures are never recomputed here.
 */

import type {
  AccountListProjection,
  CreateSetAsideCommand,
  FinanceStateProjection,
  SetAsideProjection,
  SetAsideCycleKind as ApiCycleKind,
  SetAsideKind as ApiSetAsideKind,
  TransactionProjection,
  TransactionType as ApiTransactionType,
  UpcomingEventProjection,
  UpcomingEventRecurrence,
  UpcomingEventStatus,
} from '../types';
import type { FinanceDerived } from './financeCalc';
import type {
  Account,
  AgendaItem,
  ArchivedAgenda,
  BillDue,
  CreatePosInput,
  PosCategory,
  PosItem,
  TimePeriod,
  Transaction,
} from './types';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'Mei', 'Jun', 'Jul', 'Agu', 'Sep', 'Okt', 'Nov', 'Des'];

export const ACCOUNT_ICON: Record<Account['type'], string> = {
  bank: 'account_balance',
  ewallet: 'account_balance_wallet',
  cash: 'payments',
  credit: 'credit_card',
};

export const POS_ICON: Record<PosCategory, string> = {
  saving: 'savings',
  routine_incremental: 'restaurant',
  routine_batch: 'event_repeat',
  single_spend: 'shopping_cart_checkout',
};

// ───────────────────────── date helpers ─────────────────────────

/** "2026-10-28" → "28 Okt 2026". Blank input → "Fleksibel". */
export function formatDateDisplay(dateString: string): string {
  if (!dateString) return 'Fleksibel';
  const parts = dateString.split('-');
  if (parts.length === 3) {
    const year = parts[0];
    const monthIndex = parseInt(parts[1], 10) - 1;
    const day = parseInt(parts[2], 10);
    return `${day} ${MONTHS[monthIndex] || ''} ${year}`;
  }
  return dateString;
}

/** "2026-10-28" → "28 Okt". */
export function formatDateShort(dateString: string): string {
  if (!dateString) return '';
  const parts = dateString.split('-');
  if (parts.length !== 3) return dateString;
  const monthIndex = parseInt(parts[1], 10) - 1;
  return `${parseInt(parts[2], 10)} ${MONTHS[monthIndex] || ''}`;
}

export function todayIso(): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Jakarta',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());
  const part = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((item) => item.type === type)?.value ?? '';
  return `${part('year')}-${part('month')}-${part('day')}`;
}

function timePeriodFor(isoDate: string): TimePeriod {
  if (!isoDate) return 'month';
  const target = new Date(`${isoDate}T00:00:00`);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const diffDays = Math.floor((today.getTime() - target.getTime()) / 86_400_000);
  if (diffDays <= 0) return 'today';
  if (diffDays <= 7) return 'week';
  return 'month';
}

// ───────────────────────── enum maps ─────────────────────────

export function toAccountType(
  type: AccountListProjection['accounts'][number]['type']
): Account['type'] {
  switch (type) {
    case 'Cash':
      return 'cash';
    case 'EWallet':
      return 'ewallet';
    case 'Credit':
      return 'credit';
    default:
      return 'bank';
  }
}

export function fromAccountType(type: Account['type']): 'Cash' | 'Bank' | 'EWallet' | 'Credit' {
  switch (type) {
    case 'cash':
      return 'Cash';
    case 'ewallet':
      return 'EWallet';
    case 'credit':
      return 'Credit';
    default:
      return 'Bank';
  }
}

export function toPosCategory(kind: ApiSetAsideKind | null): PosCategory {
  switch (kind) {
    case 'RoutineIncremental':
      return 'routine_incremental';
    case 'RoutineBatch':
      return 'routine_batch';
    case 'SingleSpend':
      return 'single_spend';
    // Legacy set-asides carry no kind; they are open-ended savings.
    case 'Saving':
    default:
      return 'saving';
  }
}

export function fromPosCategory(category: PosCategory): ApiSetAsideKind {
  switch (category) {
    case 'routine_incremental':
      return 'RoutineIncremental';
    case 'routine_batch':
      return 'RoutineBatch';
    case 'single_spend':
      return 'SingleSpend';
    default:
      return 'Saving';
  }
}

const CYCLE_LABEL: Record<Exclude<ApiCycleKind, 'None'>, string> = {
  Weekly: 'Mingguan',
  Monthly: 'Bulanan',
  Quarterly: 'Per 3 Bulan',
  SemiAnnual: 'Per 6 Bulan',
  Annual: 'Tahunan',
};

/** "Bulanan" / "Mingguan" / "Per 3 Bulan" / … from the FE vocabulary. */
export function toCycleLabel(cycle: ApiCycleKind): string | undefined {
  return cycle === 'None' ? undefined : CYCLE_LABEL[cycle];
}

export function fromCycleLabel(label: string | undefined): ApiCycleKind {
  switch (label) {
    case 'Mingguan':
      return 'Weekly';
    case 'Bulanan':
      return 'Monthly';
    case 'Per 3 Bulan':
      return 'Quarterly';
    case 'Per 6 Bulan':
      return 'SemiAnnual';
    case 'Tahunan':
      return 'Annual';
    default:
      return 'None';
  }
}

export function toCreateSetAsideCommand(
  input: CreatePosInput,
  sourceAccountId?: string
): CreateSetAsideCommand {
  const targetAmount = input.targetAmount ?? input.plafon ?? null;
  const cycleKind =
    input.cycleKind ??
    (input.cycle && targetAmount ? fromCycleLabel(input.cycle) : 'None');

  // Pendanaan awal bukan input user.
  // Rutinitas Bertahap, Berkala & Sekali Pakai: dana langsung disisihkan
  // = plafon/target dari uang yang belum dialokasikan.
  // Tabungan: saldo awal 0 (diisi lewat Top-Up).
  const preparesInitialFunds =
    input.category === 'routine_incremental' ||
    input.category === 'routine_batch' ||
    input.category === 'single_spend';
  const amount = preparesInitialFunds ? (targetAmount ?? 0) : (input.amount ?? 0);

  return {
    // SourceAccountId hanya divalidasi sekali pakai — TIDAK disimpan di SetAside.
    sourceAccountId: sourceAccountId ?? null,
    name: input.name,
    note: input.description || undefined,
    transactionCategory: input.transactionCategory || undefined,
    kind: fromPosCategory(input.category),
    targetAmount,
    cycleKind,
    amount,
  };
}

const RECURRENCE_LABEL: Record<UpcomingEventRecurrence, string> = {
  None: 'Satu Kali',
  Weekly: 'Mingguan',
  Monthly: 'Bulanan',
  Quarterly: 'Per 3 Bulan',
  Annual: 'Tahunan',
};

export function toRepeatLabel(recurrence: UpcomingEventRecurrence): string {
  return RECURRENCE_LABEL[recurrence];
}

export function fromRepeatLabel(repeat: string): UpcomingEventRecurrence {
  switch (repeat) {
    case 'Mingguan':
      return 'Weekly';
    case 'Bulanan':
      return 'Monthly';
    case 'Per 3 Bulan':
      return 'Quarterly';
    case 'Tahunan':
      return 'Annual';
    default:
      return 'None';
  }
}

const TYPE_LABEL: Record<ApiTransactionType, string> = {
  Income: 'Pemasukan',
  Expense: 'Pengeluaran',
  Transfer: 'Transfer',
  Refund: 'Refund',
  Reversal: 'Pembalikan',
  Adjustment: 'Penyesuaian',
};

const TYPE_ICON: Record<ApiTransactionType, string> = {
  Income: 'arrow_downward',
  Expense: 'receipt_long',
  Transfer: 'sync_alt',
  Refund: 'receipt_long',
  Reversal: 'history',
  Adjustment: 'tune',
};

// ───────────────────────── entity mapping ─────────────────────────

export function mapAccount(
  projection: AccountListProjection['accounts'][number]
): Account {
  const type = toAccountType(projection.type);
  return {
    id: projection.id,
    name: projection.name,
    // Backend deliberately keeps no Account.Role — the label stays client-only.
    role: '',
    type,
    balance: projection.actualBalance,
    availableBalance: projection.availableBalance,
    icon: ACCOUNT_ICON[type],
    archived: projection.isArchived,
  };
}

export function mapPosItem(item: SetAsideProjection): PosItem {
  const category = toPosCategory(item.kind);
  const cycle = toCycleLabel(item.cycleKind);
  const hasCycle = cycle !== undefined;

  return {
    id: item.id,
    name: item.name,
    description: item.note ?? '',
    transactionCategory: item.transactionCategory ?? undefined,
    category,
    // LEGACY ONLY — alokasi tidak terikat Sumber Dana; pos baru tanpa akun.
    accountLabel: item.accountName ?? undefined,
    // Hint non-binding untuk pre-select pada proses manual (top-up/pakai).
    defaultSourceAccountId: item.defaultSourceAccountId ?? undefined,
    amount: item.amount,
    targetAmount: item.targetAmount ?? undefined,
    // A cycling set-aside's plafon is its per-cycle target balance.
    plafon: hasCycle ? item.targetAmount ?? undefined : undefined,
    usedAmount: item.usedAmount,
    cycle,
    cycleExecuted: item.isCycleExecuted,
    cycleLabel:
      cycle && item.currentCycleEnd
        ? `${cycle} · s.d. ${formatDateShort(item.currentCycleEnd)}`
        : cycle,
    status: item.isUnderfunded ? 'Kurang pendanaan' : undefined,
    icon: POS_ICON[category],
    archived: item.status === 'Closed',
    closeReason: item.closeReason ?? undefined,
    cycleKind: item.cycleKind,
    createdAt: item.createdAt,
  };
}

export function mapAgenda(event: UpcomingEventProjection): AgendaItem {
  const isIncome = event.direction === 'Income';
  return {
    id: event.id,
    title: event.title,
    amount: event.amount,
    isIncome,
    displayDate: formatDateDisplay(event.dueDate ?? ''),
    rawDate: event.dueDate ?? '',
    // LEGACY ONLY — Rencana tidak terikat Sumber Dana; rencana baru tanpa akun.
    accountLabel: event.accountName ?? undefined,
    categoryLabel: event.categoryName ?? (isIncome ? 'Pemasukan Kas' : 'Lainnya'),
    repeat: toRepeatLabel(event.recurrence),
    type: event.scheduleKind === 'Scheduled' ? 'scheduled' : 'flexible',
    note: event.note ?? undefined,
    icon: isIncome ? 'payments' : 'receipt_long',
  };
}

const ARCHIVE_STATUS: Record<UpcomingEventStatus, string> = {
  Scheduled: 'Terjadwal',
  Realized: 'Selesai',
  Skipped: 'Dilewati',
  Cancelled: 'Dibatalkan',
};

export function mapArchivedAgenda(event: UpcomingEventProjection): ArchivedAgenda {
  return {
    id: event.id,
    title: event.title,
    amount: event.amount,
    // Only realized/skipped/cancelled events reach the archive; the date shown is
    // the event's own schedule, formatted from real data.
    date: formatDateDisplay(event.dueDate ?? ''),
    // LEGACY ONLY.
    accountLabel: event.accountName ?? undefined,
    status: ARCHIVE_STATUS[event.status],
  };
}

/**
 * Jatuh Tempo = scheduled expense events that are due or overdue.
 * Expected events never move money until they are realized, so they surface here
 * as obligations only — and leave this list the moment they are postponed.
 *
 * sourceAccountId = akun LEGACY dari data lama (opsional). Saat membayar,
 * user memilih Sumber Dana aktual di modal realizasi — rencana tidak terikat akun.
 */
export function mapBillDue(event: UpcomingEventProjection): BillDue {
  const due = event.dueDate ? formatDateShort(event.dueDate) : '';
  return {
    id: event.id,
    name: event.title,
    amount: event.amount,
    accountLabel: event.accountName ?? '-',
    meta: `${due ? `Tenggat ${due}` : 'Tanpa tanggal'}${event.accountName ? ` · legacy: ${event.accountName}` : ''}`,
    badge: event.isOverdue ? 'Terlambat' : 'Tenggat hari ini',
    categoryLabel: event.categoryName ?? 'Rutin',
    icon: event.isOverdue ? 'bolt' : 'receipt_long',
    sourceAccountId: event.accountId ?? '',
    status: 'unpaid',
  };
}

/**
 * Signed display amount for the activity feed.
 * A transfer is a movement of money, never an expense — it keeps a positive amount
 * and is labelled as a transfer instead.
 */
export function signedAmountFor(
  tx: TransactionProjection,
  relatedType: ApiTransactionType | undefined
): number {
  switch (tx.type) {
    case 'Income':
    case 'Transfer':
    case 'Refund':
      return tx.amount;
    case 'Reversal':
      // Reversing an expense credits money back; reversing an income takes it away.
      return relatedType === 'Income' ? -tx.amount : tx.amount;
    default:
      return -tx.amount;
  }
}

export function mapTransaction(
  tx: TransactionProjection,
  relatedType: ApiTransactionType | undefined
): Transaction {
  const entry = tx.entries[0];
  const amount = signedAmountFor(tx, relatedType);
  return {
    id: tx.id,
    title: tx.description || tx.categoryName || TYPE_LABEL[tx.type],
    accountLabel: entry?.accountName ?? '-',
    accountId: entry?.accountId,
    // Opsional. Alokasi Dana yang Disisihkan — independen dari Sumber Dana.
    setAsideLabel: tx.setAsideName ?? undefined,
    date: formatDateShort(tx.occurredOn),
    time: formatTime(tx.createdAt),
    dateGroup: formatDateDisplay(tx.occurredOn),
    timePeriod: timePeriodFor(tx.occurredOn),
    amount,
    category: tx.categoryName || TYPE_LABEL[tx.type],
    icon: TYPE_ICON[tx.type],
    isVoided: tx.isReversed,
    voidReason: tx.reversalReason,
    isReversal: tx.type === 'Reversal',
  };
}

function formatTime(utcTimestamp: string): string {
  const date = new Date(utcTimestamp);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleTimeString('id-ID', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  });
}

// ───────────────────────── whole-state mapping ─────────────────────────

export interface MappedFinanceState {
  derived: FinanceDerived;
  accounts: Account[];
  billsDue: BillDue[];
  posItems: PosItem[];
  posArchivedCount: number;
  agendas: AgendaItem[];
  archivedAgendas: ArchivedAgenda[];
  transactions: Transaction[];
}

/**
 * The backend is the single source of every financial figure. `derived` is read
 * straight from `FinanceStateProjection` — the client never recomputes Uang Bebas.
 */
export function mapFinanceState(
  projection: FinanceStateProjection | null
): MappedFinanceState {
  const empty: MappedFinanceState = {
    derived: {
      totalLiquidity: 0,
      billsDueTotal: 0,
      scheduledExpenseCommitments: 0,
      unpaidBillsCount: 0,
      savingsCommitment: 0,
      totalAvailable: 0,
      freeCash: 0,
      commitmentTotal: 0,
      hasUnpaidBills: false,
      allBillsPaid: false,
    },
    accounts: [],
    billsDue: [],
    posItems: [],
    posArchivedCount: 0,
    agendas: [],
    archivedAgendas: [],
    transactions: [],
  };

  if (!projection) return empty;

  const activePos = projection.setAsides.filter((s) => s.status === 'Active');
  const archivedPos = projection.setAsides.filter((s) => s.status === 'Closed');
  const scheduled = projection.upcomingEvents.filter(
    (e) => e.status === 'Scheduled'
  );
  const archived = projection.upcomingEvents.filter(
    (e) => e.status !== 'Scheduled'
  );
  const dueBills = projection.dueEvents.filter(
    (e) => e.direction === 'Expense'
  );

  const relatedTypes = new Map(
    projection.recentTransactions.map((t) => [t.id, t.type])
  );

  return {
    derived: {
      totalLiquidity: projection.totalActualBalance,
      billsDueTotal: projection.dueObligations,
      scheduledExpenseCommitments: projection.scheduledExpenseCommitments,
      unpaidBillsCount: projection.dueObligationsCount,
      savingsCommitment: projection.totalCommittedSetAside,
      // DUA ANGKA TERPISAH — jangan disamakan.
      totalAvailable: projection.totalAvailable,
      freeCash: projection.freeCash,
      commitmentTotal:
        projection.totalCommittedSetAside + projection.scheduledExpenseCommitments,
      hasUnpaidBills: projection.hasUnpaidBills,
      allBillsPaid: projection.allBillsPaid,
    },
    accounts: projection.accounts.map(mapAccount),
    billsDue: dueBills.map(mapBillDue),
    posItems: [...activePos, ...archivedPos].map(mapPosItem),
    posArchivedCount: archivedPos.length,
    agendas: scheduled.map(mapAgenda),
    archivedAgendas: archived.map(mapArchivedAgenda),
    transactions: projection.recentTransactions.map((t) =>
      mapTransaction(t, relatedTypes.get(t.relatedTransactionId ?? ''))
    ),
  };
}
