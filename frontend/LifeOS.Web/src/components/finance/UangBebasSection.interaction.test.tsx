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
        posItems={[]}
        onSave={onSave}
      />,
    );
    await user.click(screen.getByRole('button', { name: 'Rekening Aktif' }));
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
});
