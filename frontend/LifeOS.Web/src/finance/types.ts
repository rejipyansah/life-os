import type { SetAsideCycleKind } from '../types';

export type AccountType = 'bank' | 'cash' | 'ewallet' | 'credit';

export interface Account {
  id: string;
  name: string;
  role: string;
  type: AccountType;
  balance: number;
  /** Saldo tersedia = actual − set-aside. Sumber validasi funding di UI. */
  availableBalance: number;
  icon: string;
  archived: boolean;
}

export type BillStatus = 'unpaid' | 'paid' | 'postponed';

export interface BillDue {
  id: string;
  name: string;
  amount: number;
  accountLabel: string;
  meta: string;
  badge: string;
  categoryLabel: string;
  icon: string;
  sourceAccountId: string;
  status: BillStatus;
}

export type PosCategory =
  | 'saving'
  | 'routine_incremental'
  | 'routine_batch'
  | 'single_spend';

export interface PosItem {
  id: string;
  name: string;
  description: string;
  category: PosCategory;
  accountLabel: string;
  amount: number;
  targetAmount?: number;
  plafon?: number;
  usedAmount?: number;
  cycleLabel?: string;
  cycle?: string;
  status?: string;
  icon: string;
  archived?: boolean;
}

export type AgendaType = 'scheduled' | 'flexible';

export interface AgendaItem {
  id: string;
  title: string;
  amount: number;
  isIncome: boolean;
  displayDate: string;
  rawDate: string;
  accountLabel: string;
  categoryLabel: string;
  repeat: string;
  type: AgendaType;
  note?: string;
  icon: string;
}

export interface ArchivedAgenda {
  id: string;
  title: string;
  amount: number;
  date: string;
  accountLabel: string;
  status: string;
}

export type TimePeriod = 'today' | 'week' | 'month';

export interface Transaction {
  id: string;
  title: string;
  accountLabel: string;
  accountId?: string;
  date: string;
  time: string;
  dateGroup: string;
  timePeriod: TimePeriod;
  amount: number;
  category: string;
  icon: string;
  isVoided?: boolean;
  voidReason?: string | null;
  isReversal?: boolean;
}

export interface FinanceToast {
  id: number;
  message: string;
  icon?: string;
}

export interface ParsedTransaction {
  status: 'EMPTY' | 'NO_AMOUNT' | 'SUCCESS';
  amount?: number;
  type?: 'Pengeluaran' | 'Pemasukan' | 'Alokasi Pos' | 'Transfer Kas';
  category?: string;
  account?: string;
}

export interface CreatePosInput {
  name: string;
  description: string;
  category: PosCategory;
  accountLabel: string;
  targetAmount?: number;
  plafon?: number;
  cycle?: string;
  cycleKind?: SetAsideCycleKind;
  /**
   * Opsional, hanya untuk tipe yang tidak auto-fund (mis. saving).
   * Rutinitas Bertahap & Sekali Pakai: amount dihitung dari plafon/target.
   */
  amount?: number;
  icon?: string;
}

export interface CreateAgendaInput {
  title: string;
  amount: number;
  isIncome: boolean;
  rawDate: string;
  accountLabel: string;
  categoryLabel: string;
  repeat: string;
  type: AgendaType;
  note?: string;
  icon?: string;
}
