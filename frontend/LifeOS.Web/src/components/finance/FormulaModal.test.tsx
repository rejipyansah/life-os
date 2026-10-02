import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { deriveFinanceState, type Account, type BillDue } from '../../finance';
import { FormulaModal } from './UangBebasSection';

const account = (balance: number): Account => ({
  id: 'a1',
  name: 'Rekening',
  role: 'main',
  type: 'bank',
  balance,
  availableBalance: balance,
  icon: 'account_balance',
  archived: false,
});

const bill = (amount: number, status: BillDue['status'] = 'unpaid'): BillDue => ({
  id: 'b1',
  name: 'WiFi',
  amount,
  accountLabel: 'Rekening',
  meta: '',
  badge: '',
  categoryLabel: 'Tagihan',
  icon: 'wifi',
  sourceAccountId: 'a1',
  status,
});

const render = (balance: number, savings: number, billAmount: number) =>
  renderToStaticMarkup(
    <FormulaModal
      open
      onClose={() => {}}
      derived={deriveFinanceState({
        accounts: [account(balance)],
        billsDue: [bill(billAmount)],
        savingsCommitment: savings,
      })}
    />,
  );

describe('FormulaModal', () => {
  it('asks the question and answers it with one calculation', () => {
    const html = render(1_000_000, 300_000, 200_000);
    expect(html).toContain('Kenapa uang saya yang bisa dipakai cuma segini?');
    expect(html).toContain('Uang di sumber dana');
    expect(html).toContain('Rp 1.000.000');
    expect(html).toContain('Uang yang disisihkan');
    expect(html).toContain('−Rp 300.000');
    expect(html).toContain('Uang untuk kebutuhan mendatang');
    expect(html).toContain('−Rp 200.000');
    expect(html).toContain('Uang yang Bisa Dipakai');
    expect(html).toContain('Rp 500.000');
    expect(html).toContain('Oke, paham');
  });

  it('keeps the backend numbers unchanged', () => {
    const html = render(1_250_000, 400_000, 100_000);
    expect(html).toContain('Rp 1.250.000');
    expect(html).toContain('−Rp 400.000');
    expect(html).toContain('−Rp 100.000');
    expect(html).toContain('Rp 750.000');
  });

  it('drops accounting labels, long formula and extra explanations', () => {
    const html = render(1_000_000, 300_000, 200_000);
    expect(html).not.toContain('Total Likuiditas');
    expect(html).not.toContain('Komitmen');
    expect(html).not.toContain('Kewajiban');
    expect(html).not.toContain('Uang Bebas');
    expect(html).not.toContain('Lihat bagaimana angka ini dihitung');
    expect(html).not.toContain('Yang sudah diperhitungkan');
    expect(html).not.toContain('siap digunakan');
    expect(html).not.toContain('Nilai ini terupdate');
    expect(html).not.toContain('Saya Mengerti');
  });

  it('shows a negative balance without hiding the sign', () => {
    const html = render(100_000, 300_000, 200_000);
    expect(html).toContain('−Rp 400.000');
  });
});
