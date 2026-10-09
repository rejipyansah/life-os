// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { deriveFinanceState, type Account, type BillDue, type ParsedTransaction, type PosItem } from '../../finance';
import CatatTransaksiSection from './CatatTransaksiSection';
import { JatuhTempoCard } from './UangBebasSection';

afterEach(cleanup);

const accounts: Account[] = [
  {
    id: 'account-active',
    name: 'Rekening Aktif',
    role: 'main',
    type: 'bank',
    balance: 1_000_000,
    availableBalance: 1_000_000,
    icon: 'account_balance',
    archived: false,
  },
  {
    id: 'account-archived',
    name: 'Rekening Arsip',
    role: 'secondary',
    type: 'bank',
    balance: 0,
    availableBalance: 0,
    icon: 'account_balance',
    archived: true,
  },
];

const bill: BillDue = {
  id: 'bill-wifi',
  name: 'Tagihan WiFi',
  amount: 250_000,
  accountLabel: '',
  meta: 'Hari ini',
  badge: 'Hari ini',
  categoryLabel: 'Internet',
  icon: 'wifi',
  sourceAccountId: '',
  status: 'unpaid',
};

const pos: PosItem = {
  id: 'pos-home',
  name: 'Dana Rumah',
  description: '',
  category: 'saving',
  amount: 300_000,
  icon: 'savings',
  archived: false,
};

describe('JatuhTempoCard interactions', () => {
  it('requires a source account and sends the selected account and optional set-aside on payment', async () => {
    const user = userEvent.setup();
    const onPayBill = vi.fn();
    const onPayAll = vi.fn();
    const onPostponeBill = vi.fn();
    const derived = deriveFinanceState({ accounts, billsDue: [bill], savingsCommitment: pos.amount });

    render(
      <JatuhTempoCard
        derived={derived}
        billsDue={[bill]}
        accounts={accounts}
        posItems={[pos]}
        onPayBill={onPayBill}
        onPayAll={onPayAll}
        onPostponeBill={onPostponeBill}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Bayar' }));
    expect(screen.getByRole('dialog', { name: 'Bayar Tagihan — Tagihan WiFi' })).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Bayar Tagihan' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByRole('option', { name: /Rekening Arsip/ })).toBeNull();

    await user.selectOptions(screen.getByLabelText(/Sumber Dana/), 'account-active');
    await user.selectOptions(screen.getByLabelText(/Dana yang Disisihkan/), 'pos-home');
    await user.click(screen.getByRole('button', { name: 'Bayar Tagihan' }));

    expect(onPayBill).toHaveBeenCalledWith('bill-wifi', 'account-active', 'pos-home');
    expect(onPayAll).not.toHaveBeenCalled();
    expect(onPostponeBill).not.toHaveBeenCalled();
  });

  it('allows postponing from the bill row and closes the payment modal with Escape', async () => {
    const user = userEvent.setup();
    const onPayBill = vi.fn();
    const onPostponeBill = vi.fn();
    const derived = deriveFinanceState({ accounts, billsDue: [bill], savingsCommitment: 0 });

    render(
      <JatuhTempoCard
        derived={derived}
        billsDue={[bill]}
        accounts={accounts}
        posItems={[]}
        onPayBill={onPayBill}
        onPayAll={vi.fn()}
        onPostponeBill={onPostponeBill}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Tunda' }));
    expect(onPostponeBill).toHaveBeenCalledWith('bill-wifi');
    await user.click(screen.getByRole('button', { name: 'Bayar' }));
    fireEvent.keyDown(window, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(onPayBill).not.toHaveBeenCalled();
  });
});

describe('CatatTransaksiSection save flow', () => {
  async function prepareTransaction(user: ReturnType<typeof userEvent.setup>, onSave: (parsed: ParsedTransaction) => Promise<boolean>) {
    render(
      <CatatTransaksiSection
        accounts={[accounts[0]]}
        onSave={onSave}
      />,
    );
    await user.click(screen.getByRole('button', { name: /Rekening Aktif/ }));
    await user.type(screen.getByPlaceholderText(/Contoh: bayar wifi/), 'jajan kopi 25rb');
    await user.click(screen.getByRole('button', { name: 'Catat Transaksi' }));
    expect(await screen.findByRole('button', { name: 'Simpan Transaksi' })).toBeTruthy();
  }

  it('keeps the confirmation available after a failed save and shows success only after retry succeeds', async () => {
    const user = userEvent.setup();
    const onSave = vi.fn<(parsed: ParsedTransaction) => Promise<boolean>>()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true);
    await prepareTransaction(user, onSave);

    await user.click(screen.getByRole('button', { name: 'Simpan Transaksi' }));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(screen.queryByText(/Transaksi berhasil dicatat/)).toBeNull();
    expect(screen.getByRole('button', { name: 'Simpan Transaksi' })).toBeTruthy();

    await user.click(screen.getByRole('button', { name: 'Simpan Transaksi' }));
    expect(await screen.findByText(/Transaksi berhasil dicatat:.*Rp 25\.000/)).toBeTruthy();
    expect(onSave).toHaveBeenCalledTimes(2);
  });

  it('disables confirmation while the mutation is pending to prevent duplicate submissions', async () => {
    const user = userEvent.setup();
    let resolveSave!: (saved: boolean) => void;
    const onSave = vi.fn(() => new Promise<boolean>((resolve) => { resolveSave = resolve; }));
    await prepareTransaction(user, onSave);

    await user.click(screen.getByRole('button', { name: 'Simpan Transaksi' }));
    const pendingButton = await screen.findByRole('button', { name: 'Menyimpan…' }) as HTMLButtonElement;
    expect(pendingButton.disabled).toBe(true);
    await user.click(pendingButton);
    expect(onSave).toHaveBeenCalledTimes(1);

    resolveSave(true);
    expect(await screen.findByText(/Transaksi berhasil dicatat:.*Rp 25\.000/)).toBeTruthy();
  });

  it('invalidates an old preview when the transaction text changes', async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(true);
    await prepareTransaction(user, onSave);

    await user.clear(screen.getByPlaceholderText(/Contoh: bayar wifi/));
    await user.type(screen.getByPlaceholderText(/Contoh: bayar wifi/), 'beli makan 40rb');
    expect(screen.queryByRole('button', { name: 'Simpan Transaksi' })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Catat Transaksi' }));
    expect(await screen.findByText('Rp 40.000')).toBeTruthy();
  });

  it('allows correcting the category and amount before saving', async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(true);
    await prepareTransaction(user, onSave);

    await user.click(screen.getByRole('button', { name: 'Edit transaksi' }));
    expect(screen.getByRole('heading', { name: 'Edit detail transaksi' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Makan & Minum' })).toBeNull();
    await user.selectOptions(screen.getByLabelText('Kategori transaksi'), 'Belanja');
    expect(screen.getByLabelText('Nominal transaksi')).toHaveProperty('value', '25.000');
    await user.clear(screen.getByLabelText('Nominal transaksi'));
    await user.type(screen.getByLabelText('Nominal transaksi'), '30000');
    expect(screen.getByLabelText('Nominal transaksi')).toHaveProperty('value', '30.000');
    await user.click(screen.getByRole('button', { name: 'Simpan Transaksi' }));

    await waitFor(() => expect(onSave).toHaveBeenCalledWith(expect.objectContaining({
      category: 'Belanja',
      amount: 30_000,
    })));
  });

  it('locks income category to Pendapatan and excludes it from expense choices', async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(true);
    render(<CatatTransaksiSection accounts={[accounts[0]]} onSave={onSave} />);

    await user.click(screen.getByRole('button', { name: 'Input cepat' }));
    await user.click(screen.getByRole('button', { name: 'Pemasukan' }));
    const quickIncomeCategory = screen.getByLabelText('Kategori pemasukan') as HTMLInputElement;
    expect(quickIncomeCategory.value).toBe('Pendapatan');
    expect(quickIncomeCategory.readOnly).toBe(true);
    await user.click(screen.getByRole('button', { name: '+10k' }));
    await user.click(screen.getByRole('button', { name: 'Tinjau Transaksi' }));
    await user.click(screen.getByRole('button', { name: 'Edit transaksi' }));

    const incomeCategory = screen.getByLabelText('Kategori transaksi') as HTMLInputElement;
    expect(incomeCategory.value).toBe('Pendapatan');
    expect(incomeCategory.readOnly).toBe(true);

    await user.selectOptions(screen.getByLabelText('Jenis transaksi hasil'), 'Pengeluaran');
    const expenseCategory = screen.getByLabelText('Kategori transaksi') as HTMLSelectElement;
    expect(expenseCategory.value).toBe('Lainnya');
    expect(screen.queryByRole('option', { name: 'Pendapatan' })).toBeNull();

    await user.selectOptions(screen.getByLabelText('Jenis transaksi hasil'), 'Pemasukan');
    expect((screen.getByLabelText('Kategori transaksi') as HTMLInputElement).value).toBe('Pendapatan');
    await user.click(screen.getByRole('button', { name: 'Simpan Transaksi' }));
    await waitFor(() => expect(onSave).toHaveBeenCalledWith(expect.objectContaining({
      type: 'Pemasukan',
      category: 'Pendapatan',
    })));
  });

  it('offers a structured quick-entry path with amount shortcuts', async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(true);
    render(<CatatTransaksiSection accounts={[accounts[0]]} onSave={onSave} />);
    await user.click(screen.getByRole('button', { name: 'Input cepat' }));
    await user.click(screen.getByRole('button', { name: '+10k' }));
    await user.click(screen.getByRole('button', { name: '+10k' }));
    expect(screen.getByLabelText('Nominal (Rp)')).toHaveProperty('value', '20.000');
    await user.click(screen.getByRole('button', { name: 'Tinjau Transaksi' }));
    await user.click(screen.getByRole('button', { name: 'Edit transaksi' }));
    expect(await screen.findByLabelText('Nominal transaksi')).toHaveProperty('value', '20.000');
  });

  it('enforces minimum and exact-safe maximum in quick amount entry', async () => {
    const user = userEvent.setup();
    render(<CatatTransaksiSection accounts={[accounts[0]]} onSave={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Input cepat' }));
    const amount = screen.getByLabelText('Nominal (Rp)');
    fireEvent.change(amount, { target: { value: '0' } });
    expect(screen.getByRole('alert').textContent).toMatch(/minimum Rp 1/);
    expect(screen.getByRole('button', { name: 'Tinjau Transaksi' })).toHaveProperty('disabled', true);

    fireEvent.change(amount, { target: { value: '9007199254740992' } });
    expect(screen.getByRole('alert').textContent).toMatch(/maksimum/);
    expect(screen.getByRole('button', { name: 'Tinjau Transaksi' })).toHaveProperty('disabled', true);
  });

  it('limits raw transaction and description text lengths', async () => {
    const user = userEvent.setup();
    await prepareTransaction(user, vi.fn().mockResolvedValue(true));
    expect(screen.getByPlaceholderText(/Contoh: bayar wifi/)).toHaveProperty('maxLength', 512);
    await user.click(screen.getByRole('button', { name: 'Edit transaksi' }));
    expect(screen.getByLabelText('Catatan transaksi')).toHaveProperty('maxLength', 512);
  });

  it('discards uncommitted edits with Batal edit', async () => {
    const user = userEvent.setup();
    await prepareTransaction(user, vi.fn().mockResolvedValue(true));
    await user.click(screen.getByRole('button', { name: 'Edit transaksi' }));
    await user.selectOptions(screen.getByLabelText('Kategori transaksi'), 'Belanja');
    await user.click(screen.getByRole('button', { name: 'Batal edit' }));
    expect(screen.getByRole('heading', { name: 'Makan & Minum' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Edit detail transaksi' })).toBeNull();
  });

  it('closes the preview dialog without removing the entry form', async () => {
    const user = userEvent.setup();
    await prepareTransaction(user, vi.fn().mockResolvedValue(true));
    expect(screen.getByRole('dialog', { name: 'Tinjau transaksi' })).toBeTruthy();
    await user.click(screen.getByRole('button', { name: 'Batal' }));
    expect(screen.queryByRole('dialog', { name: 'Tinjau transaksi' })).toBeNull();
    expect(screen.getByPlaceholderText(/Contoh: bayar wifi/)).toBeTruthy();
  });

  it('does not expose POS selection in transaction capture', () => {
    render(<CatatTransaksiSection accounts={[accounts[0]]} onSave={vi.fn()} />);
    expect(screen.queryByText('Dana yang Disisihkan')).toBeNull();
  });

  it('shows the available account balance beside its source name', () => {
    render(<CatatTransaksiSection accounts={[accounts[0]]} onSave={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Rekening Aktif' })).toBeTruthy();
    expect(screen.queryByText(/Saldo tersedia/)).toBeNull();
  });

  it('shows each actual account balance in the source-account select while editing', async () => {
    const user = userEvent.setup();
    await prepareTransaction(user, vi.fn().mockResolvedValue(true));
    await user.click(screen.getByRole('button', { name: 'Edit transaksi' }));
    expect(screen.getByRole('option', { name: 'Rekening Aktif — saldo Rp 1.000.000' })).toBeTruthy();
    expect(screen.queryByText(/Uang Bebas:/)).toBeNull();
  });
});
