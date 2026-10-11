// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Account, AgendaItem, ArchivedAgenda, CreateAgendaInput } from '../../finance';
import AgendaKasSection from './AgendaKasSection';

afterEach(cleanup);

const account: Account = {
  id: 'account-1',
  name: 'Bank Utama',
  type: 'bank',
  balance: 2_000_000,
  availableBalance: 2_000_000,
  icon: 'account_balance',
  archived: false,
};

const flexibleIncome: AgendaItem = {
  id: 'flex-income', title: 'Bonus', amount: 500_000, isIncome: true,
  displayDate: 'Fleksibel', rawDate: '', categoryLabel: 'Pendapatan',
  repeat: 'Satu Kali', type: 'flexible', icon: 'payments',
};

const recurringExpense: AgendaItem = {
  id: 'expense-1', title: 'Internet', amount: 350_000, isIncome: false,
  displayDate: '25 Jan 2099', rawDate: '2099-01-25', categoryLabel: 'Tagihan & Utilitas',
  repeat: 'Bulanan', type: 'scheduled', defaultSourceAccountId: 'account-1',
  defaultSourceAccountLabel: 'Bank Utama', note: 'Nomor pelanggan', icon: 'receipt_long',
};

const scheduledIncome: AgendaItem = {
  id: 'income-1', title: 'Gaji', amount: 3_450_000, isIncome: true,
  displayDate: '1 Nov 2026', rawDate: '2026-11-01', categoryLabel: 'Pendapatan',
  repeat: 'Satu Kali', type: 'scheduled', defaultSourceAccountId: 'account-1',
  defaultSourceAccountLabel: 'Bank Utama', icon: 'payments',
};

function renderAgendaSection({
  agendas = [],
  archivedAgendas = [],
  onCreate = vi.fn().mockResolvedValue(true),
  onUpdate = vi.fn().mockResolvedValue(true),
  onScheduleFlexible = vi.fn().mockResolvedValue(true),
  onStopCycle = vi.fn(),
  onFinish = vi.fn().mockResolvedValue(true),
}: {
  agendas?: AgendaItem[];
  archivedAgendas?: ArchivedAgenda[];
  onCreate?: (input: CreateAgendaInput) => Promise<boolean>;
  onUpdate?: (id: string, input: CreateAgendaInput) => Promise<boolean>;
  onScheduleFlexible?: (id: string, newDueDate: string) => Promise<boolean>;
  onStopCycle?: (id: string) => void;
  onFinish?: (id: string, accountId: string) => Promise<boolean>;
} = {}) {
  const create = onCreate ?? vi.fn(async (_input: CreateAgendaInput) => true);
  const update = onUpdate ?? vi.fn(async (_id: string, _input: CreateAgendaInput) => true);
  const scheduleFlexible = onScheduleFlexible ?? vi.fn(async (_id: string, _newDueDate: string) => true);
  render(
    <AgendaKasSection
      accounts={[account]}
      agendas={agendas}
      archivedAgendas={archivedAgendas}
      filter="all"
      page={1}
      onFilterChange={vi.fn()}
      onPageChange={vi.fn()}
      onSkip={vi.fn()}
      onScheduleFlexible={scheduleFlexible}
      onStopCycle={onStopCycle}
      onFinish={onFinish}
      onCreate={create}
      onUpdate={update}
    />,
  );
  return { onCreate: create, onUpdate: update, onScheduleFlexible: scheduleFlexible };
}

describe('Buat Rencana interaksi', () => {
  it('menolak Tanggal Mulai lampau dan menetapkan tanggal minimum hari ini', async () => {
    const user = userEvent.setup();
    const { onCreate } = renderAgendaSection();

    await user.click(screen.getByRole('button', { name: 'Buat Rencana' }));
    expect((screen.getByLabelText('Siklus') as HTMLSelectElement).value).toBe('Satu Kali');
    await user.type(screen.getByLabelText('Nama Rencana *'), 'Tagihan');
    await user.type(screen.getByLabelText('Jumlah (Rp)*'), '250000');
    await user.selectOptions(screen.getByLabelText('Sumber Dana Default *'), 'account-1');

    const dateInput = screen.getByLabelText('Tanggal Mulai*') as HTMLInputElement;
    expect(dateInput.min).toBeTruthy();
    fireEvent.change(dateInput, { target: { value: '2000-01-01' } });

    expect(screen.getByRole('alert').textContent).toContain('tidak boleh sebelum hari ini');
    expect((screen.getByRole('button', { name: 'Simpan' }) as HTMLButtonElement).disabled).toBe(true);
    expect(onCreate).not.toHaveBeenCalled();
  });

  it('mengirim Sumber Dana Default yang dipilih saat membuat rencana', async () => {
    const user = userEvent.setup();
    const { onCreate } = renderAgendaSection();

    await user.click(screen.getByRole('button', { name: 'Buat Rencana' }));
    await user.type(screen.getByLabelText('Nama Rencana *'), 'Bonus');
    await user.type(screen.getByLabelText('Jumlah (Rp)*'), '100000');
    await user.selectOptions(screen.getByLabelText('Sumber Dana Default *'), 'account-1');
    fireEvent.change(screen.getByLabelText('Tanggal Mulai*'), { target: { value: '2099-10-30' } });
    await user.click(screen.getByRole('button', { name: 'Simpan' }));

    expect(onCreate).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Bonus', amount: 100_000, defaultSourceAccountId: 'account-1',
    }));
  });

  it('meminta tanggal baru saat menunda rencana fleksibel', async () => {
    const user = userEvent.setup();
    const { onScheduleFlexible } = renderAgendaSection({ agendas: [flexibleIncome] });

    await user.click(screen.getByRole('button', { name: 'Atur Tanggal' }));
    const dateInput = screen.getByLabelText('Tanggal Mulai *') as HTMLInputElement;
    expect(dateInput.min).toBeTruthy();
    fireEvent.change(dateInput, { target: { value: '2099-01-01' } });
    await user.click(screen.getByRole('button', { name: 'Jadwalkan' }));

    expect(onScheduleFlexible).toHaveBeenCalledWith('flex-income', '2099-01-01');
    expect(screen.queryByRole('dialog', { name: /Atur Tanggal/ })).toBeNull();
  });

  it('membuka form dengan nilai tersimpan dan mengirim perubahan rencana', async () => {
    const user = userEvent.setup();
    const { onUpdate } = renderAgendaSection({ agendas: [recurringExpense] });

    expect(screen.queryByRole('button', { name: 'Tunda Besok' })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    expect(screen.getByDisplayValue('350.000')).toBeTruthy();
    expect(screen.getByDisplayValue('2099-01-25')).toBeTruthy();
    expect(screen.getByDisplayValue('Nomor pelanggan')).toBeTruthy();
    await user.clear(screen.getByLabelText('Nama Rencana *'));
    await user.type(screen.getByLabelText('Nama Rencana *'), 'Internet rumah');
    await user.click(screen.getByRole('button', { name: 'Simpan Perubahan' }));

    expect(onUpdate).toHaveBeenCalledWith('expense-1', expect.objectContaining({
      title: 'Internet rumah', amount: 350_000, defaultSourceAccountId: 'account-1',
    }));
  });

  it('menghentikan siklus melalui aksi Hentikan Siklus', async () => {
    const user = userEvent.setup();
    const onStopCycle = vi.fn();
    renderAgendaSection({ agendas: [recurringExpense], onStopCycle });

    await user.click(screen.getByRole('button', { name: 'Hentikan' }));
    expect(screen.getByRole('dialog', { name: 'Hentikan Siklus Rencana?' })).toBeTruthy();
    await user.click(screen.getByRole('button', { name: 'Hentikan Siklus' }));

    expect(onStopCycle).toHaveBeenCalledWith('expense-1');
  });

  it('menampilkan sumber aktual dan default pada arsip sesuai statusnya', async () => {
    const user = userEvent.setup();
    renderAgendaSection({
      archivedAgendas: [
        {
          id: 'realized', title: 'Gaji', amount: 500_000, isIncome: true,
          date: '11 Okt 2026', status: 'Selesai', sourceAccountLabel: 'BCA', sourceAccountKind: 'actual',
        },
        {
          id: 'cancelled', title: 'Tagihan', amount: 100_000, isIncome: false,
          date: '12 Okt 2026', status: 'Dibatalkan', sourceAccountLabel: 'Mandiri', sourceAccountKind: 'default',
        },
      ],
    });

    await user.click(screen.getByRole('button', { name: 'Lihat Arsip Rencana' }));

    expect(screen.getByText('Sumber Dana Aktual: BCA')).toBeTruthy();
    expect(screen.getByText('Sumber Dana Default: Mandiri')).toBeTruthy();
  });

  it('memakai Sumber Dana Default saat menyelesaikan rencana tanpa meminta alokasi pos', async () => {
    const user = userEvent.setup();
    const onFinish = vi.fn().mockResolvedValue(true);
    renderAgendaSection({ agendas: [scheduledIncome], onFinish });

    expect(screen.getByText('Default: Bank Utama')).toBeTruthy();
    await user.click(screen.getAllByRole('button', { name: 'Sudah Masuk' })[0]);

    expect((screen.getByLabelText('Sumber Dana *') as HTMLSelectElement).value).toBe('account-1');
    expect(screen.queryByText('Dana yang Disisihkan (opsional)')).toBeNull();
    await user.click(screen.getAllByRole('button', { name: 'Sudah Masuk' })[1]);
    expect(onFinish).toHaveBeenCalledWith('income-1', 'account-1');
  });
});
