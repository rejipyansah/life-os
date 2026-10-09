// @vitest-environment jsdom

import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Account, PosItem } from '../../finance';
import YangDisisihkanSection from './YangDisisihkanSection';

afterEach(cleanup);

const account: Account = {
  id: 'account-1', name: 'Bank Utama', role: 'main', type: 'bank',
  balance: 2_000_000, availableBalance: 2_000_000, icon: 'account_balance', archived: false,
};

const saving: PosItem = {
  id: 'saving-1', name: 'Modal Nikah', description: 'Target 50 juta',
  category: 'saving', amount: 1_000_000, targetAmount: 50_000_000,
  icon: 'savings', archived: false,
};

describe('Dana tujuan interaksi', () => {
  it('membedakan pengeluaran nyata dari top up/withdraw dan mempertahankan tujuan setelah dipakai', async () => {
    const user = userEvent.setup();
    const onUseIncremental = vi.fn().mockResolvedValue(true);

    render(
      <YangDisisihkanSection
        posItems={[saving]} accounts={[account]} freeCash={500_000} filter="all" page={1}
        onFilterChange={vi.fn()} onPageChange={vi.fn()}
        onTopUp={vi.fn().mockResolvedValue(true)} onWithdraw={vi.fn().mockResolvedValue(true)}
        onUseIncremental={onUseIncremental}
        onExecuteBatch={vi.fn().mockResolvedValue(true)} onExecuteSingle={vi.fn().mockResolvedValue(true)}
        onCreate={vi.fn().mockResolvedValue(true)} onDelete={vi.fn().mockResolvedValue(true)}
        onComplete={vi.fn().mockResolvedValue(true)} onUpdate={vi.fn().mockResolvedValue(true)}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Kelola Dana' }));
    expect(screen.getByText('Top Up')).toBeTruthy();
    expect(screen.getByText('Withdraw')).toBeTruthy();
    expect(screen.getByText(/saldo rekening tetap/i)).toBeTruthy();
    expect(screen.getByText('Uang Bebas tersedia')).toBeTruthy();
    expect(screen.getByText(/500\.000/)).toBeTruthy();
    expect(screen.queryByText(/Rekening Referensi/)).toBeNull();

    const spendButtons = screen.getAllByRole('button', { name: /Belanja/ });
    await user.click(spendButtons[0]);
    const amountField = screen.getByLabelText('Nominal Pengeluaran');
    const sourceField = screen.getByLabelText(/Sumber Dana/);
    expect(Boolean(amountField.compareDocumentPosition(sourceField) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true);
    await user.selectOptions(sourceField, 'account-1');
    await user.type(screen.getByLabelText(/Keperluan/), 'Dekorasi');
    await user.click(screen.getByRole('button', { name: /semua saldo dana/i }));
    await user.click(screen.getByRole('button', { name: 'Catat Pengeluaran' }));

    expect(onUseIncremental).toHaveBeenCalledWith('saving-1', 1_000_000, 'account-1', 'Dekorasi');
    expect(screen.queryByRole('dialog', { name: /Kelola Dana/ })).toBeNull();
  });

  it('menampilkan input target hanya setelah checkbox target diaktifkan', async () => {
    const user = userEvent.setup();
    const onCreate = vi.fn().mockResolvedValue(true);

    render(
      <YangDisisihkanSection
        posItems={[]} accounts={[account]} freeCash={500_000} filter="all" page={1}
        onFilterChange={vi.fn()} onPageChange={vi.fn()}
        onTopUp={vi.fn().mockResolvedValue(true)} onWithdraw={vi.fn().mockResolvedValue(true)}
        onUseIncremental={vi.fn().mockResolvedValue(true)}
        onExecuteBatch={vi.fn().mockResolvedValue(true)} onExecuteSingle={vi.fn().mockResolvedValue(true)}
        onCreate={onCreate} onDelete={vi.fn().mockResolvedValue(true)}
        onComplete={vi.fn().mockResolvedValue(true)} onUpdate={vi.fn().mockResolvedValue(true)}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Tambah Dana' }));
    expect(screen.queryByLabelText('Target Dana (Rp)')).toBeNull();
    await user.click(screen.getByLabelText(/Dana ini punya target/));
    expect(screen.getByLabelText('Target Dana (Rp)')).toBeTruthy();
    await user.type(screen.getByLabelText(/Nama Dana/), 'Dana Darurat');
    await user.type(screen.getByLabelText('Target Dana (Rp)'), '500000');
    await user.click(screen.getByRole('button', { name: 'Simpan Pos Baru' }));

    expect(onCreate).toHaveBeenCalledWith(expect.objectContaining({
      name: 'Dana Darurat',
      category: 'saving',
      targetAmount: 500_000,
    }));
    expect(onCreate.mock.calls[0][0]).not.toHaveProperty('sourceAccountLabel');
  });

  it('menyediakan nominal semua saldo bersebelahan dengan preset pada Withdraw', async () => {
    const user = userEvent.setup();
    const onWithdraw = vi.fn().mockResolvedValue(true);

    render(
      <YangDisisihkanSection
        posItems={[saving]} accounts={[account]} freeCash={500_000} filter="all" page={1}
        onFilterChange={vi.fn()} onPageChange={vi.fn()}
        onTopUp={vi.fn().mockResolvedValue(true)} onWithdraw={onWithdraw}
        onUseIncremental={vi.fn().mockResolvedValue(true)}
        onExecuteBatch={vi.fn().mockResolvedValue(true)} onExecuteSingle={vi.fn().mockResolvedValue(true)}
        onCreate={vi.fn().mockResolvedValue(true)} onDelete={vi.fn().mockResolvedValue(true)}
        onComplete={vi.fn().mockResolvedValue(true)} onUpdate={vi.fn().mockResolvedValue(true)}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Kelola Dana' }));
    await user.click(screen.getByRole('button', { name: 'Withdraw' }));
    await user.click(screen.getByRole('button', { name: /tarik semua saldo dana/i }));
    await user.click(screen.getByRole('button', { name: 'Lepas ke Uang Bebas' }));

    expect(onWithdraw).toHaveBeenCalledWith('saving-1', 1_000_000);
  });
});
