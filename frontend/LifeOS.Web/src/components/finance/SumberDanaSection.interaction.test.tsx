// @vitest-environment jsdom

import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';

import type { Account } from '../../finance';
import SumberDanaSection from './SumberDanaSection';

afterEach(cleanup);

const accounts: Account[] = [
  {
    id: 'bank-1',
    name: 'Bank Utama',
    type: 'bank',
    balance: 1_000_000,
    availableBalance: 1_000_000,
    icon: 'account_balance',
    archived: false,
  },
  {
    id: 'cash-1',
    name: 'Tunai',
    type: 'cash',
    balance: 250_000,
    availableBalance: 250_000,
    icon: 'payments',
    archived: false,
  },
];

function renderSection(overrides: Partial<ComponentProps<typeof SumberDanaSection>> = {}) {
  const props = {
    accounts,
    onAdd: vi.fn().mockResolvedValue(true),
    onUpdate: vi.fn().mockResolvedValue(true),
    onDelete: vi.fn().mockResolvedValue(true),
    onTransfer: vi.fn().mockResolvedValue(true),
    ...overrides,
  };
  render(<SumberDanaSection {...props} />);
  return props;
}

describe('SumberDanaSection interactions', () => {
  it('keeps the account form open after a failed save and closes after success', async () => {
    const user = userEvent.setup();
    const onAdd = vi.fn().mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    renderSection({ onAdd });

    await user.click(screen.getByRole('button', { name: 'Tambah Akun' }));
    await user.type(screen.getByLabelText('Nama Sumber Dana'), '  Rekening Baru  ');
    await user.click(screen.getByRole('button', { name: 'Simpan' }));

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.getByRole('dialog', { name: 'Tambah Sumber Dana' })).toBeTruthy();
    expect(onAdd).toHaveBeenNthCalledWith(1, { name: 'Rekening Baru', type: 'bank' });

    await user.click(screen.getByRole('button', { name: 'Simpan' }));
    expect(screen.queryByRole('dialog', { name: 'Tambah Sumber Dana' })).toBeNull();
  });

  it('persists a transfer using the selected distinct accounts and formatted amount', async () => {
    const user = userEvent.setup();
    const onTransfer = vi.fn().mockResolvedValue(true);
    renderSection({ onTransfer });

    await user.click(screen.getByRole('button', { name: 'Transfer' }));
    await user.selectOptions(screen.getByLabelText('Dari Sumber Dana'), 'bank-1');
    await user.selectOptions(screen.getByLabelText('Ke Sumber Dana'), 'cash-1');
    await user.type(screen.getByLabelText('Nominal Transfer'), '125000');
    await user.click(screen.getByRole('button', { name: 'Transfer Dana' }));

    expect(onTransfer).toHaveBeenCalledWith('bank-1', 'cash-1', 125000);
    expect(screen.queryByRole('dialog', { name: 'Transfer Dana' })).toBeNull();
  });
});
