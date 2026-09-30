import { describe, it, expect } from 'vitest';
import {
  deriveFinanceState,
  formatCurrency,
  formatCurrencyRaw,
  formatSignedCurrency,
  parseFormattedNumber,
  formatNumberString,
  posCategoryLabel,
  accountTypeLabel,
} from './financeCalc';
import { financeFixture } from './financeFixture';
import { parseTransactionText, detectAccountFromText } from './parseTransaction';
import type { Account, BillDue } from './types';

function makeAccount(overrides: Partial<Account> = {}): Account {
  return {
    id: 'acct-1',
    name: 'TestBank',
    role: 'Test',
    type: 'bank',
    balance: 0,
    icon: 'account_balance',
    ...overrides,
  };
}

function makeBill(overrides: Partial<BillDue> = {}): BillDue {
  return {
    id: 'bill-1',
    name: 'WiFi',
    amount: 100_000,
    accountLabel: 'Test',
    meta: '',
    badge: '',
    categoryLabel: '',
    icon: 'wifi',
    sourceAccountId: 'acct-1',
    status: 'unpaid',
    ...overrides,
  };
}

describe('deriveFinanceState', () => {
  it('baseline: liquidity − bills − commitment = uang bebas', () => {
    const result = deriveFinanceState({
      accounts: [makeAccount({ balance: 2_000_000 })],
      billsDue: [makeBill({ amount: 300_000 })],
      savingsCommitment: 200_000,
    });
    expect(result.totalLiquidity).toBe(2_000_000);
    expect(result.billsDueTotal).toBe(300_000);
    expect(result.freeCash).toBe(2_000_000 - 300_000 - 200_000);
  });

  it('only unpaid bills count toward commitments', () => {
    const result = deriveFinanceState({
      accounts: [makeAccount({ balance: 1_000_000 })],
      billsDue: [
        makeBill({ id: 'b1', amount: 100_000, status: 'unpaid' }),
        makeBill({ id: 'b2', amount: 200_000, status: 'paid' }),
        makeBill({ id: 'b3', amount: 300_000, status: 'postponed' }),
      ],
      savingsCommitment: 0,
    });
    expect(result.billsDueTotal).toBe(100_000);
    expect(result.unpaidBillsCount).toBe(1);
    expect(result.freeCash).toBe(900_000);
  });

  it('postponed bills are excluded from today commitments', () => {
    const result = deriveFinanceState({
      accounts: [makeAccount({ balance: 500_000 })],
      billsDue: [makeBill({ amount: 400_000, status: 'postponed' })],
      savingsCommitment: 0,
    });
    expect(result.billsDueTotal).toBe(0);
    expect(result.freeCash).toBe(500_000);
  });

  it('never clamps negative free cash', () => {
    const result = deriveFinanceState({
      accounts: [makeAccount({ balance: 100_000 })],
      billsDue: [makeBill({ amount: 200_000 })],
      savingsCommitment: 50_000,
    });
    expect(result.freeCash).toBe(-150_000);
  });

  it('allBillsPaid is true when every bill is settled', () => {
    const result = deriveFinanceState({
      accounts: [makeAccount({ balance: 1_000_000 })],
      billsDue: [
        makeBill({ id: 'b1', status: 'paid' }),
        makeBill({ id: 'b2', status: 'paid' }),
      ],
      savingsCommitment: 0,
    });
    expect(result.allBillsPaid).toBe(true);
    expect(result.hasUnpaidBills).toBe(false);
  });

  it('empty bills list is not "all paid"', () => {
    const result = deriveFinanceState({
      accounts: [makeAccount({ balance: 100_000 })],
      billsDue: [],
      savingsCommitment: 0,
    });
    expect(result.allBillsPaid).toBe(false);
    expect(result.freeCash).toBe(100_000);
  });

  it('default fixture matches design values', () => {
    const result = deriveFinanceState({
      accounts: financeFixture.accounts,
      billsDue: financeFixture.billsDue,
      savingsCommitment: financeFixture.savingsCommitment,
    });
    expect(result.totalLiquidity).toBe(2_661_000);
    expect(result.billsDueTotal).toBe(615_440);
    expect(result.savingsCommitment).toBe(875_000);
    expect(result.freeCash).toBe(1_170_560);
    expect(result.unpaidBillsCount).toBe(2);
  });
});

describe('formatCurrency', () => {
  it('formats positive amounts', () => {
    expect(formatCurrency(1_170_560)).toBe('Rp 1.170.560');
  });

  it('formats negative amounts with minus sign', () => {
    expect(formatCurrency(-24_000)).toBe('−Rp 24.000');
  });

  it('formatCurrencyRaw never signs', () => {
    expect(formatCurrencyRaw(-5000)).toBe('Rp 5.000');
    expect(formatCurrencyRaw(5000)).toBe('Rp 5.000');
  });

  it('formatSignedCurrency adds explicit +/−', () => {
    expect(formatSignedCurrency(1_500_000)).toBe('+Rp 1.500.000');
    expect(formatSignedCurrency(-38_000)).toBe('-Rp 38.000');
    expect(formatSignedCurrency(0)).toBe('Rp 0');
  });
});

describe('number helpers', () => {
  it('parseFormattedNumber strips non-digits', () => {
    expect(parseFormattedNumber('1.170.560')).toBe(1170560);
    expect(parseFormattedNumber('')).toBe(0);
  });

  it('formatNumberString adds thousand separators', () => {
    expect(formatNumberString('1170560')).toBe('1.170.560');
    expect(formatNumberString('')).toBe('');
  });
});

describe('labels', () => {
  it('posCategoryLabel maps categories', () => {
    expect(posCategoryLabel('saving')).toBe('Tabungan & Simpanan');
    expect(posCategoryLabel('routine_incremental')).toBe('Rutinitas Bertahap');
    expect(posCategoryLabel('routine_batch')).toBe('Rutinitas Berkala');
    expect(posCategoryLabel('single_spend')).toBe('Sekali Pakai');
  });

  it('accountTypeLabel maps types', () => {
    expect(accountTypeLabel('bank')).toBe('Bank');
    expect(accountTypeLabel('cash')).toBe('Tunai');
  });
});

describe('parseTransactionText', () => {
  it('returns EMPTY for blank input', () => {
    expect(parseTransactionText('').status).toBe('EMPTY');
    expect(parseTransactionText('   ').status).toBe('EMPTY');
  });

  it('returns NO_AMOUNT when no nominal found', () => {
    expect(parseTransactionText('bayar wifi').status).toBe('NO_AMOUNT');
  });

  it('parses rb suffix', () => {
    const r = parseTransactionText('bayar wifi 340rb');
    expect(r.status).toBe('SUCCESS');
    expect(r.amount).toBe(340_000);
    expect(r.type).toBe('Pengeluaran');
  });

  it('parses jt suffix as income with keyword', () => {
    const r = parseTransactionText('terima freelance desain 1.5jt di BCA');
    expect(r.status).toBe('SUCCESS');
    expect(r.amount).toBe(1_500_000);
    expect(r.type).toBe('Pemasukan');
    expect(r.account).toBe('BCA Operasional');
  });

  it('detects account keywords', () => {
    expect(detectAccountFromText('bayar wifi di SeaBank')).toBe('SeaBank');
    expect(detectAccountFromText('jajan tunai')).toBe('Dompet Fisik / Tunai');
    expect(detectAccountFromText('tanpa akun')).toBeNull();
  });

  it('classifies transfer and savings keywords', () => {
    expect(parseTransactionText('transfer 500rb ke SeaBank').type).toBe('Transfer Kas');
    expect(parseTransactionText('sisihkan 500rb tabungan').type).toBe('Alokasi Pos');
  });
});
