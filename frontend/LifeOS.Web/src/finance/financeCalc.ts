/**
 * FINANCE DERIVED CALCULATIONS
 *
 * Core formula (from modul Uang Bebas):
 *   Uang Bebas = Total Likuiditas − Komitmen
 *   Komitmen = savingsCommitment (Pos)
 *            + scheduledExpenseCommitments (semua rencana pengeluaran terjadwal)
 *
 * IMPORTANT: freeCash is NEVER clamped to 0.
 * If commitments exceed liquidity, freeCash will be negative.
 *
 * Live path reads figures straight from the backend projection (apiMapping).
 * `deriveFinanceState` is a legacy/tests helper.
 */

import type {
  Account,
  AgendaItem,
  BillDue,
  PosItem,
  Transaction,
} from './types';

export interface FinanceDerived {
  totalLiquidity: number;
  billsDueTotal: number;
  scheduledExpenseCommitments: number;
  unpaidBillsCount: number;
  savingsCommitment: number;
  freeCash: number;
  commitmentTotal: number;
  hasUnpaidBills: boolean;
  allBillsPaid: boolean;
}

export function deriveFinanceState(input: {
  accounts: Account[];
  billsDue: BillDue[];
  savingsCommitment: number;
}): FinanceDerived {
  const totalLiquidity = input.accounts.reduce((sum, a) => sum + a.balance, 0);

  const unpaidBills = input.billsDue.filter((b) => b.status === 'unpaid');
  const billsDueTotal = unpaidBills.reduce((sum, b) => sum + b.amount, 0);
  const unpaidBillsCount = unpaidBills.length;

  // Legacy helper: unpaid bills stand in for planned expense commitments.
  const scheduledExpenseCommitments = billsDueTotal;

  const commitmentTotal = scheduledExpenseCommitments + input.savingsCommitment;
  const freeCash = totalLiquidity - commitmentTotal;

  return {
    totalLiquidity,
    billsDueTotal,
    scheduledExpenseCommitments,
    unpaidBillsCount,
    savingsCommitment: input.savingsCommitment,
    freeCash,
    commitmentTotal,
    hasUnpaidBills: unpaidBillsCount > 0,
    allBillsPaid: unpaidBillsCount === 0 && input.billsDue.length > 0,
  };
}

export function formatCurrency(amount: number): string {
  const abs = Math.abs(amount);
  const formatted = abs.toLocaleString('id-ID', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  });
  if (amount < 0) return `−Rp ${formatted}`;
  return `Rp ${formatted}`;
}

export function formatCurrencyRaw(amount: number): string {
  return `Rp ${Math.abs(amount).toLocaleString('id-ID', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  })}`;
}

export function formatSignedCurrency(amount: number): string {
  const abs = Math.abs(amount);
  const formatted = abs.toLocaleString('id-ID', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  });
  if (amount > 0) return `+Rp ${formatted}`;
  if (amount < 0) return `-Rp ${formatted}`;
  return `Rp ${formatted}`;
}

export function parseFormattedNumber(value: string): number {
  const clean = (value || '').replace(/\D/g, '');
  return clean ? parseInt(clean, 10) : 0;
}

export function formatNumberString(value: string): string {
  const clean = value.replace(/\D/g, '');
  if (!clean) return '';
  return parseInt(clean, 10).toLocaleString('id-ID');
}

/** Nominal cepat untuk chip input — urut dari kecil ke besar, pecahan alami Indonesia. */
export const QUICK_AMOUNTS = [
  1_000, 2_000, 5_000, 10_000, 20_000, 50_000, 100_000,
] as const;

/** Label chip ringkas tanpa "Rp": 1000 → "+1k". Input & nilai transaksi tetap Rupiah penuh. */
export function quickAmountLabel(amount: number): string {
  const thousands = amount / 1000;
  return `+${Number.isInteger(thousands) ? thousands : thousands.toFixed(1)}k`;
}

/** Warna progress bar: sisa dana terhadap plafon. */
export type ProgressTone = 'green' | 'yellow' | 'red';

export interface IncrementalStatus {
  /** Batas aman siklus (plafon, fallback ke targetAmount). */
  limit: number;
  /** Sisa dana — tidak pernah negatif. */
  remaining: number;
  /** Sisa dana terhadap plafon, 0–100. */
  remainingPct: number;
  tone: ProgressTone;
  /** Nominal kelebihan di atas plafon: max(0, used − limit). */
  overage: number;
}

/**
 * Status visual Rutinitas Bertahap terhadap plafon siklus.
 *
 * - bar menunjukkan SISA dana / plafon, bukan total pemakaian;
 * - sisa dibatasi ≥ 0 (overspend tidak pernah tampil negatif / overflow);
 * - overage = kelebihan pemakaian, selalu ditampilkan sebagai nominal
 *   ketika > 0; pemakaian melewati plafon tetap boleh (ditutup Uang Bebas).
 */
export function incrementalPlafonStatus(input: {
  amount: number;
  plafon?: number;
  targetAmount?: number;
  usedAmount?: number;
}): IncrementalStatus {
  const limit = input.plafon ?? input.targetAmount ?? 0;
  const remaining = Math.max(0, input.amount);
  const ratio = limit > 0 ? remaining / limit : 0;
  return {
    limit,
    remaining,
    remainingPct: Math.min(100, Math.round(ratio * 100)),
    // >50% hijau · 25–50% kuning · <25% merah (0% merah & kosong).
    tone: ratio > 0.5 ? 'green' : ratio >= 0.25 ? 'yellow' : 'red',
    overage: limit > 0 ? Math.max(0, (input.usedAmount ?? 0) - limit) : 0,
  };
}

export function posCategoryLabel(category: PosItem['category']): string {
  switch (category) {
    case 'saving':
      return 'Tabungan & Simpanan';
    case 'routine_incremental':
      return 'Rutinitas Bertahap';
    case 'routine_batch':
      return 'Rutinitas Berkala';
    case 'single_spend':
      return 'Sekali Pakai';
    default:
      return 'Pos Dana';
  }
}

export function accountTypeLabel(type: Account['type']): string {
  switch (type) {
    case 'bank':
      return 'Bank';
    case 'cash':
      return 'Tunai';
    case 'ewallet':
      return 'E-Wallet';
    case 'credit':
      return 'Kartu';
    default:
      return 'Kas';
  }
}

export function agendaTypeLabel(type: AgendaItem['type']): string {
  return type === 'scheduled' ? 'Terjadwal' : 'Fleksibel';
}

export function transactionAmountLabel(tx: Transaction): string {
  return formatSignedCurrency(tx.amount);
}
