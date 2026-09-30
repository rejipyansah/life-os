/**
 * FINANCE DERIVED CALCULATIONS
 *
 * Core formula (from modul Uang Bebas):
 *   Uang Bebas = Total Likuiditas − Komitmen Hari Ini
 *   Komitmen Hari Ini = Σ(bill unpaid & due today) + savingsCommitment
 *
 * IMPORTANT: freeCash is NEVER clamped to 0.
 * If commitments exceed liquidity, freeCash will be negative.
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

  const commitmentTotal = billsDueTotal + input.savingsCommitment;
  const freeCash = totalLiquidity - commitmentTotal;

  return {
    totalLiquidity,
    billsDueTotal,
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
