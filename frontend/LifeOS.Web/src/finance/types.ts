import type { SetAsideCycleKind } from '../types';

export type AccountType = 'bank' | 'cash' | 'ewallet' | 'credit';

export interface Account {
  id: string;
  name: string;
  role: string;
  type: AccountType;
  balance: number;
  /** Saldo aktual rekening; hanya membatasi transaksi uang riil dari rekening ini. */
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
  /**
   * LEGACY ONLY — alokasi tidak terikat Sumber Dana.
   * Hanya ditampilkan untuk data lama; pos baru tidak memiliki akun.
   */
  accountLabel?: string;
  /**
   * Hint non-binding: sumber dana default untuk proses manual (top-up/pakai).
   * Pre-select UI saja — bukan ikatan pos ke rekening.
   */
  defaultSourceAccountId?: string;
  amount: number;
  targetAmount?: number;
  plafon?: number;
  usedAmount?: number;
  cycleLabel?: string;
  cycle?: string;
  /** Rutinitas berkala sudah dipakai pada cycle berjalan. */
  cycleExecuted?: boolean;
  status?: string;
  icon: string;
  archived?: boolean;
  closeReason?: string;
  cycleKind?: SetAsideCycleKind;
  createdAt?: string;
}

export type AgendaType = 'scheduled' | 'flexible';

export interface AgendaItem {
  id: string;
  title: string;
  amount: number;
  isIncome: boolean;
  displayDate: string;
  rawDate: string;
  /**
   * LEGACY ONLY — Rencana tidak terikat Sumber Dana.
   * Hanya ditampilkan untuk data lama; rencana baru tidak memiliki akun.
   */
  accountLabel?: string;
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
  /** LEGACY ONLY. */
  accountLabel?: string;
  status: string;
}

export type TimePeriod = 'today' | 'week' | 'month';

export interface Transaction {
  id: string;
  title: string;
  accountLabel: string;
  accountId?: string;
  /**
   * Opsional. Dana yang Disisihkan (pos) yang dialokasikan/dilepas.
   * INDEPENDEN dari accountLabel (Sumber Dana).
   */
  setAsideLabel?: string;
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
  status: 'EMPTY' | 'NO_AMOUNT' | 'AMBIGUOUS_AMOUNT' | 'AMOUNT_OUT_OF_RANGE' | 'SUCCESS';
  amount?: number;
  type?: 'Pengeluaran' | 'Pemasukan' | 'Alokasi Pos' | 'Transfer Kas';
  /** Kategori inti yang dapat dikoreksi pengguna sebelum disimpan. */
  category?: string;
  /** Rincian bebas dari teks transaksi, terpisah dari kategori inti. */
  description?: string;
  /** Sumber Dana — uang keluar/masuk dari mana. */
  account?: string;
}

export interface CreatePosInput {
  name: string;
  description: string;
  category: PosCategory;
  /**
   * Opsional. Rekening referensi/preferensi; tidak didebit dan bukan batas pendanaan.
   */
  sourceAccountLabel?: string;
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
  categoryLabel: string;
  repeat: string;
  type: AgendaType;
  note?: string;
  icon?: string;
}
