import {
  addToSetAside,
  cancelUpcomingEvent,
  closeSetAside,
  createAccount,
  createSetAside,
  createTransaction,
  createUpcomingEvent,
  postponeUpcomingEvent,
  realizeUpcomingEvent,
  reverseTransaction,
  skipUpcomingEvent,
  spendFromSetAside,
  updateAccount as updateAccountRequest,
  updateSetAside,
  updateUpcomingEvent,
  withdrawFromSetAside,
} from '../api';
import type {
  SetAsideCloseReason,
  SetAsideCycleKind,
  UpdateUpcomingEventCommand,
} from '../types';
import { fromAccountType, fromRepeatLabel, toCreateSetAsideCommand, todayIso } from './apiMapping';
import type { MappedFinanceState } from './apiMapping';
import type {
  Account,
  CreateAgendaInput,
  CreatePosInput,
  ParsedTransaction,
  PosItem,
} from './types';

type PushToast = (message: string, icon?: string) => void;
type MutationRunner = (action: () => Promise<unknown>, onSuccess?: () => void) => Promise<boolean>;

interface MutationSupport {
  run: MutationRunner;
  pushToast: PushToast;
}

function formatRupiah(amount: number): string {
  return new Intl.NumberFormat('id-ID', {
    style: 'currency',
    currency: 'IDR',
    maximumFractionDigits: 0,
  }).format(amount);
}

export function createBillActions(
  billsDue: MappedFinanceState['billsDue'],
  { run, pushToast }: MutationSupport,
  refresh: () => void,
) {
  return {
    payBill: (id: string, accountId: string, setAsideId?: string) => {
      const bill = billsDue.find((item) => item.id === id);
      return run(
        () => realizeUpcomingEvent(id, {
          accountId,
          setAsideId: setAsideId ?? null,
          occurredOn: todayIso(),
        }),
        () => {
          if (bill) pushToast(`Berhasil membayar ${bill.name}`, 'task_alt');
        },
      );
    },

    payAllBills: async (accountId: string, setAsideId?: string): Promise<boolean> => {
      const unpaid = billsDue.filter((bill) => bill.status === 'unpaid');
      if (unpaid.length === 0) {
        pushToast('Semua tagihan hari ini sudah lunas sebelumnya.', 'info');
        return false;
      }

      let succeeded = 0;
      let failed = 0;
      for (const bill of unpaid) {
        try {
          await realizeUpcomingEvent(bill.id, {
            accountId,
            setAsideId: setAsideId ?? null,
            occurredOn: todayIso(),
          });
          succeeded++;
        } catch {
          failed++;
        }
      }

      refresh();
      if (failed === 0) {
        pushToast(`Seluruh tagihan hari ini (${succeeded} tagihan) telah lunas terbayar!`, 'check_circle');
      } else {
        pushToast(`${succeeded} tagihan berhasil dibayar; ${failed} gagal. Daftar telah diperbarui.`, 'error');
      }
      return succeeded > 0;
    },

    postponeBill: (id: string) => {
      const bill = billsDue.find((item) => item.id === id);
      void run(
        () => postponeUpcomingEvent(id, {}),
        () => {
          if (bill) pushToast(`Jatuh tempo untuk "${bill.name}" ditunda ke esok hari`, 'event_repeat');
        },
      );
    },
  };
}

export function createAccountActions(
  accounts: MappedFinanceState['accounts'],
  { run, pushToast }: MutationSupport,
) {
  return {
    addAccount: (data: { name: string; type: Account['type'] }) =>
      run(
        () => createAccount({ name: data.name, type: fromAccountType(data.type) }),
        () => pushToast(`Sumber Dana "${data.name}" ditambahkan.`),
      ),

    updateAccount: (id: string, data: { name: string; type: Account['type'] }) =>
      run(
        () => updateAccountRequest(id, { name: data.name, type: fromAccountType(data.type) }),
        () => pushToast(`Sumber Dana "${data.name}" diperbarui.`),
      ),

    deleteAccount: (id: string) => {
      const target = accounts.find((account) => account.id === id);
      return run(
        () => updateAccountRequest(id, { isArchived: true }),
        () => {
          if (target) pushToast(`Sumber Dana "${target.name}" diarsipkan.`, 'delete');
        },
      );
    },

    transfer: (fromId: string, toId: string, amount: number) => {
      const target = accounts.find((account) => account.id === toId);
      return run(
        () => createTransaction({
          type: 'Transfer',
          amount,
          occurredOn: todayIso(),
          entries: [
            { accountId: fromId, amount: -amount },
            { accountId: toId, amount },
          ],
        }),
        () => pushToast(
          `Transfer ${formatRupiah(amount)} ke ${target?.name ?? 'sumber dana'} berhasil!`,
          'swap_horiz',
        ),
      );
    },
  };
}

export function createTransactionActions(
  resolveAccountId: (name: string | undefined) => string | undefined,
  { run, pushToast }: MutationSupport,
) {
  return {
    saveTransaction: async (parsed: ParsedTransaction): Promise<boolean> => {
      if (parsed.status !== 'SUCCESS' || !parsed.amount) return false;

      const accountId = resolveAccountId(parsed.account);
      if (parsed.type === 'Alokasi Pos') {
        pushToast('Untuk mengelola alokasi, gunakan modul Dana yang Disisihkan.', 'info');
        return false;
      }
      if (parsed.type === 'Transfer Kas') {
        pushToast('Transfer butuh rekening tujuan. Gunakan menu Transfer di Sumber Dana.', 'info');
        return false;
      }
      if (!accountId) {
        pushToast('Sumber Dana tidak ditemukan.', 'error');
        return false;
      }

      const isIncome = parsed.type === 'Pemasukan';
      const amount = parsed.amount;
      return run(
        () => createTransaction({
          type: isIncome ? 'Income' : 'Expense',
          amount,
          description: parsed.description || parsed.category || undefined,
          categoryName: parsed.category || (isIncome ? 'Pendapatan' : undefined),
          occurredOn: parsed.occurredOn || todayIso(),
          entries: [{ accountId, amount: isIncome ? amount : -amount }],
        }),
        () => pushToast(
          `${isIncome ? 'Pemasukan' : 'Pengeluaran'} ${formatRupiah(amount)} tercatat.${isIncome ? ' Jika ada kekurangan pendanaan siklus, sebagian Uang Bebas dapat otomatis dipakai untuk mengisinya.' : ''}`,
        ),
      );
    },

    voidTransaction: (id: string, reason: string) => {
      void run(
        () => reverseTransaction(id, { reason }),
        () => pushToast('Transaksi berhasil dibatalkan.', 'check_circle'),
      );
    },
  };
}

export function createSetAsideActions(
  posItems: PosItem[],
  resolveAccountId: (name: string | undefined) => string | undefined,
  { run, pushToast }: MutationSupport,
) {
  const getPos = (id: string) => posItems.find((pos) => pos.id === id);

  const spendFromPos = (
    id: string,
    amount: number,
    sourceAccountId: string,
    note: string | undefined,
    successMessage: (pos: PosItem) => string,
  ) => {
    const pos = getPos(id);
    if (amount <= 0) return Promise.resolve(false);
    return run(
      () => spendFromSetAside(id, {
        sourceAccountId,
        amount,
        description: note || pos?.name || undefined,
        occurredOn: todayIso(),
        note: note || undefined,
      }),
      () => {
        if (pos) pushToast(successMessage(pos));
      },
    );
  };

  return {
    topUpPos: (id: string, amount: number, sourceAccountId?: string) => {
      if (amount <= 0) return Promise.resolve(false);
      const pos = getPos(id);
      return run(
        () => addToSetAside(id, { amount, sourceAccountId: sourceAccountId ?? null }),
        () => {
          if (pos) pushToast(`Alokasi "${pos.name}" bertambah ${formatRupiah(amount)} dari Uang Bebas. Saldo rekening tidak berubah.`);
        },
      );
    },

    withdrawPos: (id: string, amount: number) => {
      if (amount <= 0) return Promise.resolve(false);
      const pos = getPos(id);
      const actual = Math.min(amount, pos?.amount ?? amount);
      return run(
        () => withdrawFromSetAside(id, { amount }),
        () => {
          if (pos) pushToast(`${formatRupiah(actual)} dilepas dari alokasi "${pos.name}" ke Uang Bebas. Saldo rekening tidak berubah.`);
        },
      );
    },

    useIncrementalPos: (id: string, amount: number, sourceAccountId: string, note?: string) =>
      spendFromPos(id, amount, sourceAccountId, note, (pos) =>
        `Pemakaian ${formatRupiah(amount)} dicatat dari "${pos.name}".`),

    executeBatchPos: (id: string, actualCost: number, sourceAccountId: string, note?: string) =>
      spendFromPos(id, actualCost, sourceAccountId, note, (pos) =>
        `Eksekusi rutinitas "${pos.name}" diselesaikan (${formatRupiah(actualCost)}).`),

    executeSingleSpendPos: (id: string, actualCost: number, sourceAccountId: string, note?: string) => {
      const pos = getPos(id);
      const amount = actualCost > 0 ? actualCost : pos?.amount ?? 0;
      return spendFromPos(id, amount, sourceAccountId, note, (target) =>
        `Pos "${target.name}" direalisasikan & diarsipkan.`);
    },

    createPos: (input: CreatePosInput) => {
      const sourceAccountId = resolveAccountId(input.sourceAccountLabel);
      return run(
        () => createSetAside(toCreateSetAsideCommand(input, sourceAccountId)),
        () => pushToast(`Pos "${input.name}" berhasil dibuat.`),
      );
    },

    deletePos: (id: string) => {
      const pos = getPos(id);
      return run(
        () => closeSetAside(id, { reason: 'Cancelled' satisfies SetAsideCloseReason }),
        () => {
          if (pos) pushToast(`Pos "${pos.name}" ditutup. Dana yang tersisa dikembalikan ke kas.`, 'delete');
        },
      );
    },

    completePos: (id: string) => {
      const pos = getPos(id);
      return run(
        () => closeSetAside(id, { reason: 'Spent' satisfies SetAsideCloseReason }),
        () => {
          if (pos) pushToast(`Tujuan "${pos.name}" diselesaikan. Sisa alokasi kembali ke Uang Bebas.`);
        },
      );
    },

    updatePos: (id: string, command: {
      name: string;
      note: string;
      transactionCategory: string;
      targetAmount?: number;
      removeTarget?: boolean;
      cycleKind: SetAsideCycleKind;
    }) => run(
      () => updateSetAside(id, command),
      () => pushToast('Detail Dana yang Disisihkan diperbarui.'),
    ),
  };
}

export function createAgendaActions(
  agendas: MappedFinanceState['agendas'],
  { run, pushToast }: MutationSupport,
) {
  const getAgenda = (id: string) => agendas.find((agenda) => agenda.id === id);

  return {
    skipAgenda: (id: string) => {
      const agenda = getAgenda(id);
      void run(
        () => skipUpcomingEvent(id, {}),
        () => {
          if (agenda) pushToast(`Agenda "${agenda.title}" telah dilewati.`, 'info');
        },
      );
    },

    scheduleFlexibleAgenda: (id: string, newDueDate: string) => {
      const agenda = getAgenda(id);
      return run(
        () => postponeUpcomingEvent(id, { newDueDate }),
        () => {
          if (agenda) pushToast(`Agenda "${agenda.title}" dijadwalkan pada ${newDueDate}.`);
        },
      );
    },

    cancelAgenda: (id: string) => {
      const agenda = getAgenda(id);
      void run(
        () => cancelUpcomingEvent(id, {}),
        () => {
          if (agenda) pushToast(`Siklus agenda "${agenda.title}" dihentikan dan dipindahkan ke arsip.`, 'inventory_2');
        },
      );
    },

    finishAgenda: (id: string, accountId: string) => {
      const agenda = getAgenda(id);
      return run(
        () => realizeUpcomingEvent(id, { accountId, setAsideId: null, occurredOn: todayIso() }),
        () => {
          if (agenda) pushToast(`Agenda "${agenda.title}" berhasil diselesaikan!`, 'task_alt');
        },
      );
    },

    createAgenda: (input: CreateAgendaInput) => {
      const dueDate = input.rawDate || null;
      return run(
        () => createUpcomingEvent({
          accountId: null,
          defaultSourceAccountId: input.defaultSourceAccountId,
          title: input.title,
          amount: input.amount,
          direction: input.isIncome ? 'Income' : 'Expense',
          categoryName: input.categoryLabel || undefined,
          note: input.note || undefined,
          dueDate,
          scheduleKind: input.type === 'flexible' || !dueDate ? 'Flexible' : 'Scheduled',
          recurrence: fromRepeatLabel(input.repeat),
        }),
        () => pushToast(`Agenda "${input.title}" berhasil disimpan.`),
      );
    },

    updateAgenda: (id: string, input: CreateAgendaInput) => {
      const agenda = getAgenda(id);
      if (!agenda) return Promise.resolve(false);

      const dueDate = input.rawDate || null;
      const command: UpdateUpcomingEventCommand = {
        title: input.title,
        amount: input.amount,
        direction: input.isIncome ? 'Income' : 'Expense',
        categoryName: input.categoryLabel,
        note: input.note ?? '',
        defaultSourceAccountId: input.defaultSourceAccountId,
        scheduleKind: input.type === 'flexible' ? 'Flexible' : 'Scheduled',
        recurrence: fromRepeatLabel(input.repeat),
        ...(dueDate === null
          ? { clearDueDate: true }
          : dueDate !== agenda.rawDate
            ? { dueDate }
            : {}),
      };

      return run(
        () => updateUpcomingEvent(id, command),
        () => pushToast(`Agenda "${input.title}" berhasil diperbarui.`),
      );
    },
  };
}
