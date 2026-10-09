import { useCallback, useEffect, useMemo, useState } from 'react';

import {
  createAccount,
  createSetAside,
  createTransaction,
  createUpcomingEvent,
  deleteUpcomingEvent,
  getFinanceState,
  postponeUpcomingEvent,
  realizeUpcomingEvent,
  reverseTransaction,
  skipUpcomingEvent,
  spendFromSetAside,
  addToSetAside,
  withdrawFromSetAside,
  closeSetAside,
  updateSetAside,
  updateAccount as updateAccountRequest,
} from '../api';
import type {
  FinanceStateProjection,
  SetAsideCloseReason,
} from '../types';
import type { FinanceDerived } from './financeCalc';
import {
  fromAccountType,
  fromRepeatLabel,
  mapFinanceState,
  toCreateSetAsideCommand,
  todayIso,
} from './apiMapping';
import type {
  Account,
  AgendaItem,
  ArchivedAgenda,
  BillDue,
  CreateAgendaInput,
  CreatePosInput,
  FinanceToast,
  ParsedTransaction,
  PosCategory,
  PosItem,
  TimePeriod,
  Transaction,
} from './types';

const POS_PER_PAGE = 5;
const AGENDA_PER_PAGE = 5;
const ACTIVITY_PER_PAGE = 5;

let toastSeq = 0;

function formatRupiah(amount: number): string {
  return new Intl.NumberFormat('id-ID', {
    style: 'currency',
    currency: 'IDR',
    maximumFractionDigits: 0,
  }).format(amount);
}

function errorMessage(error: unknown): string {
  if (error instanceof Error && error.message) return error.message;
  return 'Terjadi kesalahan. Silakan coba lagi.';
}

export interface FinanceStateApi {
  derived: FinanceDerived;
  accounts: Account[];
  billsDue: BillDue[];
  posItems: PosItem[];
  posArchivedCount: number;
  agendas: AgendaItem[];
  archivedAgendas: ArchivedAgenda[];
  transactions: Transaction[];
  toasts: FinanceToast[];

  /* Filter / pagination state */
  posFilter: PosCategory | 'all';
  posPage: number;
  agendaFilter: 'all' | 'scheduled' | 'flexible';
  agendaPage: number;
  activityFilter: TimePeriod | 'all';
  activityPage: number;

  setPosFilter: (f: PosCategory | 'all') => void;
  setPosPage: (p: number) => void;
  setAgendaFilter: (f: 'all' | 'scheduled' | 'flexible') => void;
  setAgendaPage: (p: number) => void;
  setActivityFilter: (f: TimePeriod | 'all') => void;
  setActivityPage: (p: number) => void;

  /* Actions */
  dismissToast: (id: number) => void;
  pushToast: (message: string, icon?: string) => void;

  payBill: (id: string, accountId: string, setAsideId?: string) => void;
  payAllBills: (accountId: string, setAsideId?: string) => void;
  postponeBill: (id: string) => void;

  addAccount: (data: { name: string; role: string; type: Account['type'] }) => void;
  updateAccount: (id: string, data: { name: string; role: string; type: Account['type'] }) => void;
  deleteAccount: (id: string) => void;
  transfer: (fromId: string, toId: string, amount: number) => void;

  saveTransaction: (parsed: ParsedTransaction) => Promise<boolean>;
  voidTransaction: (id: string, reason: string) => void;

  topUpPos: (id: string, amount: number, sourceAccountId?: string) => Promise<boolean>;
  withdrawPos: (id: string, amount: number) => Promise<boolean>;
  /**
   * Pakai pos: sourceAccountId = Sumber Dana tempat uang BENAR-BENAR keluar.
   * Wajib — seluruh nominal keluar dari akun itu; pos melepas min(amount, saldoPos).
   */
  useIncrementalPos: (
    id: string,
    amount: number,
    sourceAccountId: string,
    note?: string
  ) => Promise<boolean>;
  executeBatchPos: (
    id: string,
    actualCost: number,
    sourceAccountId: string,
    note?: string
  ) => Promise<boolean>;
  executeSingleSpendPos: (
    id: string,
    actualCost: number,
    sourceAccountId: string,
    note?: string
  ) => Promise<boolean>;
  createPos: (input: CreatePosInput) => Promise<boolean>;
  deletePos: (id: string) => Promise<boolean>;
  updatePos: (id: string, command: { name: string; note: string; targetAmount?: number; removeTarget?: boolean; cycleKind: import('../types').SetAsideCycleKind }) => Promise<boolean>;

  skipAgenda: (id: string) => void;
  postponeAgenda: (id: string) => void;
  deleteAgenda: (id: string) => void;
  /** Selesaikan rencana: accountId wajib (Sumber Dana aktual), setAsideId opsional. */
  finishAgenda: (id: string, accountId: string, setAsideId?: string) => void;
  createAgenda: (input: CreateAgendaInput) => void;
}

export function useFinanceState(): FinanceStateApi {
  const [projection, setProjection] = useState<FinanceStateProjection | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const [posFilter, setPosFilter] = useState<PosCategory | 'all'>('all');
  const [posPage, setPosPage] = useState(1);
  const [agendaFilter, setAgendaFilter] = useState<'all' | 'scheduled' | 'flexible'>('all');
  const [agendaPage, setAgendaPage] = useState(1);
  const [activityFilter, setActivityFilter] = useState<TimePeriod | 'all'>('all');
  const [activityPage, setActivityPage] = useState(1);
  const [toasts, setToasts] = useState<FinanceToast[]>([]);

  /* ── Read model ──────────────────────────────────────────────
   * Every figure below is derived on the backend. The client only
   * formats it — it never recomputes financial state.
   */
  useEffect(() => {
    let cancelled = false;
    getFinanceState()
      .then((next) => {
        if (!cancelled) setProjection(next);
      })
      .catch(() => {
        // Keep the last known projection; the UI already renders empty state.
      });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const mapped = useMemo(() => mapFinanceState(projection), [projection]);

  const refresh = useCallback(() => setReloadKey((k) => k + 1), []);

  const pushToast = useCallback((message: string, icon?: string) => {
    const id = ++toastSeq;
    setToasts((prev) => [...prev, { id, message, icon }]);
    window.setTimeout(() => {
      setToasts((prev) => prev.filter((t) => t.id !== id));
    }, 3200);
  }, []);

  const dismissToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  /** Runs a backend mutation, refreshes the read model, and reports failures. */
  const run = useCallback(
    async (action: () => Promise<unknown>, onSuccess?: () => void): Promise<boolean> => {
      try {
        await action();
        onSuccess?.();
        refresh();
        return true;
      } catch (error) {
        pushToast(errorMessage(error), 'error');
        return false;
      }
    },
    [pushToast, refresh]
  );

  const resolveAccountId = useCallback(
    (name: string | undefined): string | undefined => {
      if (!name) return undefined;
      const wanted = name.trim().toLowerCase();
      // HANYA cocokkan nama eksplisit — tanpa fallback ke akun pertama.
      // Sumber Dana dipilih user, bukan ditebak sistem.
      return mapped.accounts.find((a) => a.name.toLowerCase() === wanted)?.id;
    },
    [mapped.accounts]
  );

  /* ── Jatuh Tempo ────────────────────────────────────────────
   * A bill is a due upcoming cash event. Paying it realizes it into a
   * real transaction — user memilih Sumber Dana aktual saat membayar.
   * Rencana tidak terikat akun; akun dipilih di modal realizasi.
   */
  const payBill = useCallback(
    (id: string, accountId: string, setAsideId?: string) => {
      const bill = mapped.billsDue.find((b) => b.id === id);
      void run(
        () =>
          realizeUpcomingEvent(id, {
            accountId,
            setAsideId: setAsideId ?? null,
            occurredOn: todayIso(),
          }),
        () => {
          if (bill) pushToast(`Berhasil membayar ${bill.name}`, 'task_alt');
        }
      );
    },
    [mapped.billsDue, pushToast, run]
  );

  const payAllBills = useCallback(
    (accountId: string, setAsideId?: string) => {
      const unpaid = mapped.billsDue.filter((b) => b.status === 'unpaid');
      if (unpaid.length === 0) {
        pushToast('Semua tagihan hari ini sudah lunas sebelumnya.', 'info');
        return;
      }
      void run(
        async () => {
          for (const bill of unpaid) {
            await realizeUpcomingEvent(bill.id, {
              accountId,
              setAsideId: setAsideId ?? null,
              occurredOn: todayIso(),
            });
          }
        },
        () =>
          pushToast(
            `Seluruh tagihan hari ini (${unpaid.length} tagihan) telah lunas terbayar!`,
            'check_circle'
          )
      );
    },
    [mapped.billsDue, pushToast, run]
  );

  const postponeBill = useCallback(
    (id: string) => {
      const bill = mapped.billsDue.find((b) => b.id === id);
      void run(
        () => postponeUpcomingEvent(id, {}),
        () => {
          if (bill) {
            pushToast(
              `Jatuh tempo untuk "${bill.name}" ditunda ke esok hari`,
              'event_repeat'
            );
          }
        }
      );
    },
    [mapped.billsDue, pushToast, run]
  );

  /* ── Sumber Dana ──────────────────────────────────────────── */

  const addAccount = useCallback(
    (data: { name: string; role: string; type: Account['type'] }) => {
      void run(
        () =>
          createAccount({
            name: data.name,
            type: fromAccountType(data.type),
          }),
        () => pushToast(`Sumber Dana "${data.name}" ditambahkan.`)
      );
    },
    [pushToast, run]
  );

  const updateAccount = useCallback(
    (id: string, data: { name: string; role: string; type: Account['type'] }) => {
      void run(
        () =>
          updateAccountRequest(id, {
            name: data.name,
            type: fromAccountType(data.type),
          }),
        () => pushToast(`Sumber Dana "${data.name}" diperbarui.`)
      );
    },
    [pushToast, run]
  );

  const deleteAccount = useCallback(
    (id: string) => {
      const target = mapped.accounts.find((a) => a.id === id);
      // Accounts are archived, never hard-deleted, so history stays traceable.
      void run(
        () => updateAccountRequest(id, { isArchived: true }),
        () => {
          if (target) {
            pushToast(`Sumber Dana "${target.name}" diarsipkan.`, 'delete');
          }
        }
      );
    },
    [mapped.accounts, pushToast, run]
  );

  const transfer = useCallback(
    (fromId: string, toId: string, amount: number) => {
      const target = mapped.accounts.find((a) => a.id === toId);
      void run(
        () =>
          createTransaction({
            type: 'Transfer',
            amount,
            occurredOn: todayIso(),
            entries: [
              { accountId: fromId, amount: -amount },
              { accountId: toId, amount },
            ],
          }),
        () =>
          pushToast(
            `Transfer ${formatRupiah(amount)} ke ${target?.name ?? 'sumber dana'} berhasil!`,
            'swap_horiz'
          )
      );
    },
    [mapped.accounts, pushToast, run]
  );

  /* ── Catat Transaksi ──────────────────────────────────────── */

  const saveTransaction = useCallback(
    async (parsed: ParsedTransaction) => {
      if (parsed.status !== 'SUCCESS' || !parsed.amount) return false;

      const accountId = resolveAccountId(parsed.account);

      // Set-aside intent: a reservation, never a transaction.
      if (parsed.type === 'Alokasi Pos') {
        return run(
          () =>
            createSetAside({
              // SourceAccountId hanya divalidasi sekali pakai — pos tidak terikat akun.
              sourceAccountId: accountId ?? null,
              name: parsed.category || 'Pos Dana',
              amount: parsed.amount,
              kind: 'Saving',
            }),
          () =>
            pushToast(
              `${formatRupiah(parsed.amount!)} disisihkan ke pos "${parsed.category}".`
            )
        );
      }

      // The natural-input box has no destination field, so a real transfer
      // cannot be expressed there. Use Transfer on Sumber Dana instead.
      if (parsed.type === 'Transfer Kas') {
        pushToast(
          'Transfer butuh rekening tujuan. Gunakan menu Transfer di Sumber Dana.',
          'info'
        );
        return false;
      }

      if (!accountId) {
        pushToast('Sumber Dana tidak ditemukan.', 'error');
        return false;
      }

      const isIncome = parsed.type === 'Pemasukan';
      return run(
        () =>
          createTransaction({
            type: isIncome ? 'Income' : 'Expense',
            amount: parsed.amount!,
            description: parsed.category || undefined,
            categoryName: isIncome ? 'Pemasukan' : parsed.category || undefined,
            occurredOn: todayIso(),
            entries: [
              {
                accountId,
                amount: isIncome ? parsed.amount! : -parsed.amount!,
              },
            ],
            // Dana yang Disisihkan (opsional) — independen dari Sumber Dana.
            setAsideId: parsed.setAsideId ?? null,
          }),
        () =>
          pushToast(
            parsed.setAsideLabel
              ? `${isIncome ? 'Pemasukan' : 'Pengeluaran'} ${formatRupiah(parsed.amount!)} tercatat (pos "${parsed.setAsideLabel}").`
              : `${isIncome ? 'Pemasukan' : 'Pengeluaran'} ${formatRupiah(parsed.amount!)} tercatat.`
          )
      );
    },
    [pushToast, resolveAccountId, run]
  );

  const voidTransaction = useCallback(
    (id: string, reason: string) => {
      void run(
        () => reverseTransaction(id, { reason }),
        () => pushToast('Transaksi berhasil dibatalkan.', 'check_circle')
      );
    },
    [pushToast, run]
  );

  /* ── Yang Disisihkan ──────────────────────────────────────── */

  const topUpPos = useCallback(
    (id: string, amount: number, sourceAccountId?: string) => {
      if (amount <= 0) return Promise.resolve(false);
      const pos = mapped.posItems.find((p) => p.id === id);
      return run(
        () =>
          addToSetAside(id, {
            amount,
            // SourceAccountId hanya divalidasi sekali pakai — bukan ikatan pos.
            sourceAccountId: sourceAccountId ?? null,
          }),
        () => {
          if (pos) {
            pushToast(
              `Simpanan "${pos.name}" bertambah ${formatRupiah(amount)}.`
            );
          }
        }
      );
    },
    [mapped.posItems, pushToast, run]
  );

  const withdrawPos = useCallback(
    (id: string, amount: number) => {
      if (amount <= 0) return Promise.resolve(false);
      const pos = mapped.posItems.find((p) => p.id === id);
      const actual = Math.min(amount, pos?.amount ?? amount);
      return run(
        () => withdrawFromSetAside(id, { amount }),
        () => {
          if (pos) {
            pushToast(
              `Dana "${pos.name}" ditarik ${formatRupiah(actual)} kembali ke kas.`
            );
          }
        }
      );
    },
    [mapped.posItems, pushToast, run]
  );

  const useIncrementalPos = useCallback(
    (id: string, amount: number, sourceAccountId: string, note?: string) => {
      if (amount <= 0) return Promise.resolve(false);
      const pos = mapped.posItems.find((p) => p.id === id);
      return run(
        () =>
          spendFromSetAside(id, {
            // Sumber Dana tempat uang BENAR-BENAR keluar. Wajib.
            sourceAccountId,
            amount,
            description: note || pos?.name || undefined,
            occurredOn: todayIso(),
            note: note || undefined,
          }),
        () => {
          if (pos) {
            pushToast(
              `Pemakaian ${formatRupiah(amount)} dicatat dari "${pos.name}".`
            );
          }
        }
      );
    },
    [mapped.posItems, pushToast, run]
  );

  const executeBatchPos = useCallback(
    (id: string, actualCost: number, sourceAccountId: string, note?: string) => {
      if (actualCost <= 0) return Promise.resolve(false);
      const pos = mapped.posItems.find((p) => p.id === id);
      return run(
        () =>
          spendFromSetAside(id, {
            sourceAccountId,
            amount: actualCost,
            description: note || pos?.name || undefined,
            occurredOn: todayIso(),
            note: note || undefined,
          }),
        () => {
          if (pos) {
            pushToast(
              `Eksekusi rutinitas "${pos.name}" diselesaikan (${formatRupiah(actualCost)}).`
            );
          }
        }
      );
    },
    [mapped.posItems, pushToast, run]
  );

  const executeSingleSpendPos = useCallback(
    (id: string, actualCost: number, sourceAccountId: string, note?: string) => {
      const pos = mapped.posItems.find((p) => p.id === id);
      const amount = actualCost > 0 ? actualCost : pos?.amount ?? 0;
      if (amount <= 0) return Promise.resolve(false);
      return run(
        () =>
          spendFromSetAside(id, {
            sourceAccountId,
            amount,
            description: note || pos?.name || undefined,
            occurredOn: todayIso(),
            note: note || undefined,
          }),
        () => {
          if (pos) {
            pushToast(`Pos "${pos.name}" direalisasikan & diarsipkan.`);
          }
        }
      );
    },
    [mapped.posItems, pushToast, run]
  );

  const createPos = useCallback(
    (input: CreatePosInput) => {
      const sourceAccountId = resolveAccountId(input.sourceAccountLabel);

      return run(
        () => createSetAside(toCreateSetAsideCommand(input, sourceAccountId)),
        () => pushToast(`Pos "${input.name}" berhasil dibuat.`)
      );
    },
    [pushToast, resolveAccountId, run]
  );

  const deletePos = useCallback(
    (id: string) => {
      const pos = mapped.posItems.find((p) => p.id === id);
      return run(
        () =>
          closeSetAside(id, {
            reason: 'Cancelled' as SetAsideCloseReason,
          }),
        () => {
          if (pos) {
            pushToast(
              `Pos "${pos.name}" ditutup. Dana yang tersisa dikembalikan ke kas.`,
              'delete'
            );
          }
        }
      );
    },
    [mapped.posItems, pushToast, run]
  );

  const updatePos = useCallback(
    (id: string, command: { name: string; note: string; targetAmount?: number; removeTarget?: boolean; cycleKind: import('../types').SetAsideCycleKind }) =>
      run(() => updateSetAside(id, command), () => pushToast('Detail Dana yang Disisihkan diperbarui.')),
    [pushToast, run]
  );

  /* ── Rencana Pengeluaran/Pemasukan ─────────────────────────────────── */

  const skipAgenda = useCallback(
    (id: string) => {
      const agenda = mapped.agendas.find((a) => a.id === id);
      void run(
        () => skipUpcomingEvent(id, {}),
        () => {
          if (agenda) pushToast(`Agenda "${agenda.title}" telah dilewati.`, 'info');
        }
      );
    },
    [mapped.agendas, pushToast, run]
  );

  const postponeAgenda = useCallback(
    (id: string) => {
      const agenda = mapped.agendas.find((a) => a.id === id);
      void run(
        () => postponeUpcomingEvent(id, {}),
        () => {
          if (agenda) pushToast(`Agenda "${agenda.title}" ditunda ke besok.`);
        }
      );
    },
    [mapped.agendas, pushToast, run]
  );

  const deleteAgenda = useCallback(
    (id: string) => {
      const agenda = mapped.agendas.find((a) => a.id === id);
      void run(
        () => deleteUpcomingEvent(id),
        () => {
          if (agenda) pushToast(`Agenda "${agenda.title}" telah dihapus.`);
        }
      );
    },
    [mapped.agendas, pushToast, run]
  );

  const finishAgenda = useCallback(
    (id: string, accountId: string, setAsideId?: string) => {
      const agenda = mapped.agendas.find((a) => a.id === id);
      void run(
        () =>
          realizeUpcomingEvent(id, {
            // Sumber Dana dipilih SAAT realizasi — bukan terikat permanen.
            accountId,
            setAsideId: setAsideId ?? null,
            occurredOn: todayIso(),
          }),
        () => {
          if (agenda) {
            pushToast(`Agenda "${agenda.title}" berhasil diselesaikan!`, 'task_alt');
          }
        }
      );
    },
    [mapped.agendas, pushToast, run]
  );

  const createAgenda = useCallback(
    (input: CreateAgendaInput) => {
      const dueDate = input.rawDate || null;
      void run(
        () =>
          createUpcomingEvent({
            // Rencana TIDAK terikat Sumber Dana — akun dipilih saat realizasi.
            accountId: null,
            title: input.title,
            amount: input.amount,
            direction: input.isIncome ? 'Income' : 'Expense',
            categoryName: input.categoryLabel || undefined,
            note: input.note || undefined,
            dueDate,
            scheduleKind:
              input.type === 'flexible' || !dueDate ? 'Flexible' : 'Scheduled',
            recurrence: fromRepeatLabel(input.repeat),
          }),
        () => pushToast(`Agenda "${input.title}" berhasil disimpan.`)
      );
    },
    [pushToast, run]
  );

  return {
    derived: mapped.derived,
    accounts: mapped.accounts,
    billsDue: mapped.billsDue,
    posItems: mapped.posItems,
    posArchivedCount: mapped.posArchivedCount,
    agendas: mapped.agendas,
    archivedAgendas: mapped.archivedAgendas,
    transactions: mapped.transactions,
    toasts,

    posFilter,
    posPage,
    agendaFilter,
    agendaPage,
    activityFilter,
    activityPage,

    setPosFilter: (f) => {
      setPosFilter(f);
      setPosPage(1);
    },
    setPosPage,
    setAgendaFilter: (f) => {
      setAgendaFilter(f);
      setAgendaPage(1);
    },
    setAgendaPage,
    setActivityFilter: (f) => {
      setActivityFilter(f);
      setActivityPage(1);
    },
    setActivityPage,

    dismissToast,
    pushToast,

    payBill,
    payAllBills,
    postponeBill,

    addAccount,
    updateAccount,
    deleteAccount,
    transfer,

    saveTransaction,
    voidTransaction,

    topUpPos,
    withdrawPos,
    useIncrementalPos,
    executeBatchPos,
    executeSingleSpendPos,
    createPos,
    deletePos,
    updatePos,

    skipAgenda,
    postponeAgenda,
    deleteAgenda,
    finishAgenda,
    createAgenda,
  };
}

export { POS_PER_PAGE, AGENDA_PER_PAGE, ACTIVITY_PER_PAGE };
