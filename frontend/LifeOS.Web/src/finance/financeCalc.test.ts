import { describe, it, expect } from 'vitest';
import {
  deriveFinanceState,
  formatCurrency,
  formatCurrencyRaw,
  formatSignedCurrency,
  incrementalPlafonStatus,
  parseFormattedNumber,
  formatNumberString,
  posCategoryLabel,
  accountTypeLabel,
  quickAmountLabel,
  QUICK_AMOUNTS,
} from './financeCalc';
import { parseTransactionText, detectAccountFromText } from './parseTransaction';
import type { Account, BillDue } from './types';

function makeAccount(overrides: Partial<Account> = {}): Account {
  return {
    id: 'acct-1',
    name: 'TestBank',
    role: 'Test',
    type: 'bank',
    balance: 0,
    availableBalance: 0,
    icon: 'account_balance',
    archived: false,
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

describe('incrementalPlafonStatus', () => {
  const plafon = 300_000;

  it('bar = sisa dana terhadap plafon, dengan warna bertahap', () => {
    expect(incrementalPlafonStatus({ amount: 300_000, plafon })).toMatchObject({
      remaining: 300_000,
      remainingPct: 100,
      tone: 'green',
      overage: 0,
    });
    expect(incrementalPlafonStatus({ amount: 100_000, plafon })).toMatchObject({
      remainingPct: 33,
      tone: 'yellow',
    });
    expect(incrementalPlafonStatus({ amount: 50_000, plafon })).toMatchObject({
      remainingPct: 17,
      tone: 'red',
    });
    expect(incrementalPlafonStatus({ amount: 0, plafon })).toMatchObject({
      remaining: 0,
      remainingPct: 0,
      tone: 'red',
      overage: 0,
    });
  });

  it('tone thresholds: >50% green, 25–50% yellow, <25% red', () => {
    expect(incrementalPlafonStatus({ amount: 151_000, plafon }).tone).toBe('green');
    expect(incrementalPlafonStatus({ amount: 150_000, plafon }).tone).toBe('yellow');
    expect(incrementalPlafonStatus({ amount: 75_000, plafon }).tone).toBe('yellow');
    expect(incrementalPlafonStatus({ amount: 74_000, plafon }).tone).toBe('red');
  });

  it('overspend stays usable: sisa clamps to 0, overage reported', () => {
    const status = incrementalPlafonStatus({
      amount: 0,
      plafon,
      usedAmount: 350_000,
    });
    expect(status.limit).toBe(300_000);
    expect(status.remaining).toBe(0);
    expect(status.remainingPct).toBe(0);
    expect(status.tone).toBe('red');
    expect(status.overage).toBe(50_000);
  });

  it('overspend kecil: plafon 25k, terpakai 35k → sisa 0, kelebihan 10k', () => {
    const status = incrementalPlafonStatus({
      amount: 0,
      plafon: 25_000,
      usedAmount: 35_000,
    });
    expect(status.limit).toBe(25_000);
    expect(status.remaining).toBe(0);
    expect(status.remainingPct).toBe(0);
    expect(status.tone).toBe('red');
    expect(status.overage).toBe(10_000);
  });

  it('kelebihan = max(0, used − targetAmount) saat plafon tidak ter-mapping', () => {
    const status = incrementalPlafonStatus({
      amount: 0,
      targetAmount: 300_000,
      usedAmount: 350_000,
    });
    expect(status.limit).toBe(300_000);
    expect(status.overage).toBe(50_000);
    expect(status.remainingPct).toBe(0);
    expect(status.tone).toBe('red');
  });

  it('tanpa overspend: overage 0 (warning tidak boleh muncul)', () => {
    expect(
      incrementalPlafonStatus({ amount: 0, plafon, usedAmount: plafon }).overage
    ).toBe(0);
    expect(
      incrementalPlafonStatus({ amount: 50_000, plafon, usedAmount: 250_000 }).overage
    ).toBe(0);
  });

  it('never reports a negative sisa, even with negative saldo', () => {
    const status = incrementalPlafonStatus({ amount: -40_000, plafon, usedAmount: 340_000 });
    expect(status.remaining).toBe(0);
    expect(status.remainingPct).toBe(0);
    expect(status.tone).toBe('red');
    expect(status.overage).toBe(40_000);
  });

  it('handles a pos without plafon', () => {
    expect(incrementalPlafonStatus({ amount: 50_000 })).toMatchObject({
      remaining: 50_000,
      remainingPct: 0,
      overage: 0,
    });
  });
});

describe('quick amounts', () => {
  it('urut kecil ke besar dengan label ringkas tanpa Rp', () => {
    expect([...QUICK_AMOUNTS]).toEqual([
      1_000, 2_000, 5_000, 10_000, 20_000, 50_000, 100_000,
    ]);
    expect(QUICK_AMOUNTS.every((v, i, a) => i === 0 || a[i - 1] < v)).toBe(true);

    expect(quickAmountLabel(1_000)).toBe('+1k');
    expect(quickAmountLabel(2_000)).toBe('+2k');
    expect(quickAmountLabel(5_000)).toBe('+5k');
    expect(quickAmountLabel(10_000)).toBe('+10k');
    expect(quickAmountLabel(20_000)).toBe('+20k');
    expect(quickAmountLabel(50_000)).toBe('+50k');
    expect(quickAmountLabel(100_000)).toBe('+100k');
    expect(QUICK_AMOUNTS.every((v) => !quickAmountLabel(v).includes('Rp'))).toBe(true);
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
