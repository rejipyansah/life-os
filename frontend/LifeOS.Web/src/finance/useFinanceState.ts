import { useCallback, useMemo, useState } from 'react';

import { financeFixture } from './financeFixture';
import { deriveFinanceState } from './financeCalc';
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

function cloneAccounts(list: Account[]): Account[] {
  return list.map((a) => ({ ...a }));
}

function cloneBills(list: BillDue[]): BillDue[] {
  return list.map((b) => ({ ...b }));
}

function clonePos(list: PosItem[]): PosItem[] {
  return list.map((p) => ({ ...p }));
}

function cloneAgendas(list: AgendaItem[]): AgendaItem[] {
  return list.map((a) => ({ ...a }));
}

function cloneTransactions(list: Transaction[]): Transaction[] {
  return list.map((t) => ({ ...t }));
}

function formatDateDisplay(dateString: string): string {
  if (!dateString) return 'Fleksibel';
  const parts = dateString.split('-');
  if (parts.length === 3) {
    const year = parts[0];
    const monthIndex = parseInt(parts[1], 10) - 1;
    const day = parseInt(parts[2], 10);
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'Mei', 'Jun', 'Jul', 'Agu', 'Sep', 'Okt', 'Nov', 'Des'];
    return `${day} ${months[monthIndex] || ''} ${year}`;
  }
  return dateString;
}

export interface FinanceStateApi {
  derived: ReturnType<typeof deriveFinanceState>;
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

  payBill: (id: string) => void;
  payAllBills: () => void;
  postponeBill: (id: string) => void;

  addAccount: (data: { name: string; role: string; type: Account['type'] }) => void;
  updateAccount: (id: string, data: { name: string; role: string; type: Account['type'] }) => void;
  deleteAccount: (id: string) => void;
  transfer: (fromId: string, toId: string, amount: number) => void;

  saveTransaction: (parsed: ParsedTransaction) => void;
  voidTransaction: (id: string, reason: string) => void;

  topUpPos: (id: string, amount: number) => void;
  withdrawPos: (id: string, amount: number) => void;
  useIncrementalPos: (id: string, amount: number, note?: string) => void;
  executeBatchPos: (id: string, actualCost: number, note?: string) => void;
  executeSingleSpendPos: (id: string, actualCost: number, note?: string) => void;
  createPos: (input: CreatePosInput) => void;
  deletePos: (id: string) => void;

  skipAgenda: (id: string) => void;
  postponeAgenda: (id: string) => void;
  deleteAgenda: (id: string) => void;
  finishAgenda: (id: string) => void;
  createAgenda: (input: CreateAgendaInput) => void;
}

export function useFinanceState(): FinanceStateApi {
  const [accounts, setAccounts] = useState<Account[]>(() =>
    cloneAccounts(financeFixture.accounts)
  );
  const [billsDue, setBillsDue] = useState<BillDue[]>(() =>
    cloneBills(financeFixture.billsDue)
  );
  const [posItems, setPosItems] = useState<PosItem[]>(() =>
    clonePos(financeFixture.posItems)
  );
  const [agendas, setAgendas] = useState<AgendaItem[]>(() =>
    cloneAgendas(financeFixture.agendas)
  );
  const [archivedAgendas, setArchivedAgendas] = useState<ArchivedAgenda[]>(() =>
    financeFixture.archivedAgendas.map((a) => ({ ...a }))
  );
  const [transactions, setTransactions] = useState<Transaction[]>(() =>
    cloneTransactions(financeFixture.transactions)
  );
  const [toasts, setToasts] = useState<FinanceToast[]>([]);

  const [posFilter, setPosFilter] = useState<PosCategory | 'all'>('all');
  const [posPage, setPosPage] = useState(1);
  const [agendaFilter, setAgendaFilter] = useState<'all' | 'scheduled' | 'flexible'>('all');
  const [agendaPage, setAgendaPage] = useState(1);
  const [activityFilter, setActivityFilter] = useState<TimePeriod | 'all'>('all');
  const [activityPage, setActivityPage] = useState(1);

  const derived = useMemo(
    () =>
      deriveFinanceState({
        accounts,
        billsDue,
        savingsCommitment: financeFixture.savingsCommitment,
      }),
    [accounts, billsDue]
  );

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

  /* ── Uang Bebas / Bills ─────────────────────────── */

  const payBill = useCallback(
    (id: string) => {
      setBillsDue((prev) => {
        const bill = prev.find((b) => b.id === id);
        if (!bill || bill.status !== 'unpaid') return prev;
        const next = prev.map((b) =>
          b.id === id ? { ...b, status: 'paid' as const } : b
        );
        setAccounts((accs) =>
          accs.map((a) =>
            a.id === bill.sourceAccountId
              ? { ...a, balance: a.balance - bill.amount }
              : a
          )
        );
        pushToast(`Berhasil membayar ${bill.name}`, 'task_alt');
        return next;
      });
    },
    [pushToast]
  );

  const payAllBills = useCallback(() => {
    let paidCount = 0;
    setBillsDue((prev) => {
      const unpaid = prev.filter((b) => b.status === 'unpaid');
      if (unpaid.length === 0) return prev;
      paidCount = unpaid.length;
      const next = prev.map((b) =>
        b.status === 'unpaid' ? { ...b, status: 'paid' as const } : b
      );
      setAccounts((accs) => {
        let map = new Map(accs.map((a) => [a.id, a]));
        unpaid.forEach((b) => {
          const acc = map.get(b.sourceAccountId);
          if (acc) {
            map.set(b.sourceAccountId, {
              ...acc,
              balance: acc.balance - b.amount,
            });
          }
        });
        return Array.from(map.values());
      });
      return next;
    });
    if (paidCount > 0) {
      pushToast(
        `Seluruh tagihan hari ini (${paidCount} tagihan) telah lunas terbayar!`,
        'check_circle'
      );
    } else {
      pushToast('Semua tagihan hari ini sudah lunas sebelumnya.', 'info');
    }
  }, [pushToast]);

  const postponeBill = useCallback(
    (id: string) => {
      setBillsDue((prev) => {
        const bill = prev.find((b) => b.id === id);
        if (!bill || bill.status !== 'unpaid') return prev;
        pushToast(
          `Jatuh tempo untuk "${bill.name}" ditunda ke esok hari`,
          'event_repeat'
        );
        return prev.map((b) =>
          b.id === id ? { ...b, status: 'postponed' as const } : b
        );
      });
    },
    [pushToast]
  );

  /* ── Sumber Dana ────────────────────────────────── */

  const addAccount = useCallback(
    (data: { name: string; role: string; type: Account['type'] }) => {
      const iconMap: Record<Account['type'], string> = {
        bank: 'account_balance',
        ewallet: 'account_balance_wallet',
        cash: 'payments',
        credit: 'credit_card',
      };
      setAccounts((prev) => [
        ...prev,
        {
          id: `acct-${Date.now()}`,
          name: data.name,
          role: data.role || 'Kas Operasional',
          type: data.type,
          balance: 0,
          icon: iconMap[data.type] || 'account_balance_wallet',
        },
      ]);
      pushToast(`Rekening "${data.name}" ditambahkan.`);
    },
    [pushToast]
  );

  const updateAccount = useCallback(
    (id: string, data: { name: string; role: string; type: Account['type'] }) => {
      setAccounts((prev) =>
        prev.map((a) => (a.id === id ? { ...a, ...data } : a))
      );
      pushToast(`Rekening "${data.name}" diperbarui.`);
    },
    [pushToast]
  );

  const deleteAccount = useCallback(
    (id: string) => {
      setAccounts((prev) => {
        const target = prev.find((a) => a.id === id);
        if (target) pushToast(`Rekening "${target.name}" telah dihapus.`, 'delete');
        return prev.filter((a) => a.id !== id);
      });
    },
    [pushToast]
  );

  const transfer = useCallback(
    (fromId: string, toId: string, amount: number) => {
      setAccounts((prev) => {
        const from = prev.find((a) => a.id === fromId);
        const to = prev.find((a) => a.id === toId);
        if (!from || !to || fromId === toId || amount <= 0 || from.balance < amount) {
          return prev;
        }
        pushToast(
          `Transfer ${new Intl.NumberFormat('id-ID', {
            style: 'currency',
            currency: 'IDR',
            maximumFractionDigits: 0,
          }).format(amount)} ke ${to.name} berhasil!`,
          'swap_horiz'
        );
        return prev.map((a) => {
          if (a.id === fromId) return { ...a, balance: a.balance - amount };
          if (a.id === toId) return { ...a, balance: a.balance + amount };
          return a;
        });
      });
    },
    [pushToast]
  );

  /* ── Catat Transaksi ────────────────────────────── */

  const saveTransaction = useCallback(
    (parsed: ParsedTransaction) => {
      if (parsed.status !== 'SUCCESS' || !parsed.amount || !parsed.account) return;

      const account = accounts.find((a) => a.name === parsed.account);
      const accountId = account?.id;
      const isIncome = parsed.type === 'Pemasukan';
      const signedAmount = isIncome ? parsed.amount : -parsed.amount;

      const newTx: Transaction = {
        id: `tx-${Date.now()}`,
        title: parsed.category || 'Transaksi',
        accountLabel: parsed.account,
        accountId,
        date: 'Hari Ini, baru saja',
        time: 'baru saja',
        dateGroup: 'Hari Ini — 24 Okt 2024',
        timePeriod: 'today',
        amount: signedAmount,
        category: parsed.type === 'Pemasukan' ? 'Pemasukan' : parsed.category || 'Lainnya',
        icon: isIncome ? 'arrow_downward' : 'receipt_long',
      };

      setTransactions((prev) => [newTx, ...prev]);
      if (accountId) {
        setAccounts((prev) =>
          prev.map((a) =>
            a.id === accountId
              ? { ...a, balance: a.balance + signedAmount }
              : a
          )
        );
      }
    },
    [accounts]
  );

  const voidTransaction = useCallback(
    (id: string, reason: string) => {
      setTransactions((prev) => {
        const target = prev.find((t) => t.id === id);
        if (!target || target.isVoided) return prev;

        const reversal: Transaction = {
          id: `rev-${Date.now()}`,
          title: `Pembalikan: ${target.title} [Dibatalkan]`,
          accountLabel: target.accountLabel,
          accountId: target.accountId,
          date: 'Hari Ini, baru saja',
          time: 'baru saja',
          dateGroup: 'Hari Ini — 24 Okt 2024',
          timePeriod: 'today',
          amount: -target.amount,
          category: target.category,
          icon: 'history',
          isReversal: true,
        };

        pushToast('Transaksi berhasil dibatalkan.', 'check_circle');

        return [
          reversal,
          ...prev.map((t) =>
            t.id === id
              ? { ...t, isVoided: true, voidReason: reason }
              : t
          ),
        ];
      });
    },
    [pushToast]
  );

  /* ── Yang Disisihkan ────────────────────────────── */

  const topUpPos = useCallback(
    (id: string, amount: number) => {
      if (amount <= 0) return;
      setPosItems((prev) => {
        const pos = prev.find((p) => p.id === id);
        if (!pos) return prev;
        pushToast(
          `Simpanan "${pos.name}" bertambah ${new Intl.NumberFormat('id-ID', {
            style: 'currency',
            currency: 'IDR',
            maximumFractionDigits: 0,
          }).format(amount)}.`
        );
        return prev.map((p) =>
          p.id === id ? { ...p, amount: p.amount + amount } : p
        );
      });
    },
    [pushToast]
  );

  const withdrawPos = useCallback(
    (id: string, amount: number) => {
      if (amount <= 0) return;
      setPosItems((prev) => {
        const pos = prev.find((p) => p.id === id);
        if (!pos) return prev;
        const actual = Math.min(amount, pos.amount);
        pushToast(
          `Dana "${pos.name}" ditarik ${new Intl.NumberFormat('id-ID', {
            style: 'currency',
            currency: 'IDR',
            maximumFractionDigits: 0,
          }).format(actual)} kembali ke kas.`
        );
        return prev.map((p) =>
          p.id === id ? { ...p, amount: p.amount - actual } : p
        );
      });
    },
    [pushToast]
  );

  const useIncrementalPos = useCallback(
    (id: string, amount: number, _note?: string) => {
      if (amount <= 0) return;
      setPosItems((prev) => {
        const pos = prev.find((p) => p.id === id);
        if (!pos) return prev;
        const actual = Math.min(amount, pos.amount);
        pushToast(
          `Pemakaian ${new Intl.NumberFormat('id-ID', {
            style: 'currency',
            currency: 'IDR',
            maximumFractionDigits: 0,
          }).format(actual)} dicatat dari "${pos.name}".`
        );
        return prev.map((p) =>
          p.id === id
            ? {
                ...p,
                amount: p.amount - actual,
                usedAmount: (p.usedAmount || 0) + actual,
              }
            : p
        );
      });
    },
    [pushToast]
  );

  const executeBatchPos = useCallback(
    (id: string, actualCost: number, _note?: string) => {
      if (actualCost <= 0) return;
      setPosItems((prev) => {
        const pos = prev.find((p) => p.id === id);
        if (!pos) return prev;
        pushToast(
          `Eksekusi rutinitas "${pos.name}" diselesaikan (${new Intl.NumberFormat('id-ID', {
            style: 'currency',
            currency: 'IDR',
            maximumFractionDigits: 0,
          }).format(actualCost)}).`
        );
        return prev.map((p) =>
          p.id === id
            ? {
                ...p,
                amount: 0,
                usedAmount: (p.usedAmount || 0) + actualCost,
                status: 'Selesai periode ini',
              }
            : p
        );
      });
    },
    [pushToast]
  );

  const executeSingleSpendPos = useCallback(
    (id: string, _actualCost: number, _note?: string) => {
      setPosItems((prev) => {
        const pos = prev.find((p) => p.id === id);
        if (!pos) return prev;
        pushToast(`Pos "${pos.name}" direalisasikan & diarsipkan.`);
        return prev.filter((p) => p.id !== id);
      });
    },
    [pushToast]
  );

  const createPos = useCallback(
    (input: CreatePosInput) => {
      const iconMap: Record<PosCategory, string> = {
        saving: 'savings',
        routine_incremental: 'restaurant',
        routine_batch: 'event_repeat',
        single_spend: 'shopping_cart_checkout',
      };
      setPosItems((prev) => [
        ...prev,
        {
          id: `pos-${Date.now()}`,
          name: input.name,
          description: input.description || 'Pos dana baru',
          category: input.category,
          accountLabel: input.accountLabel,
          amount: input.category === 'routine_incremental' ? input.plafon || 0 : 0,
          targetAmount: input.targetAmount,
          plafon: input.plafon,
          cycle: input.cycle,
          icon: input.icon || iconMap[input.category],
        },
      ]);
      pushToast(`Pos "${input.name}" berhasil dibuat.`);
    },
    [pushToast]
  );

  const deletePos = useCallback(
    (id: string) => {
      setPosItems((prev) => {
        const pos = prev.find((p) => p.id === id);
        if (pos) pushToast(`Pos "${pos.name}" dihapus. Dana dikembalikan ke kas.`, 'delete');
        return prev.filter((p) => p.id !== id);
      });
    },
    [pushToast]
  );

  /* ── Agenda Kas Mendatang ───────────────────────── */

  const skipAgenda = useCallback(
    (id: string) => {
      setAgendas((prev) => {
        const idx = prev.findIndex((a) => a.id === id);
        if (idx === -1) return prev;
        const a = prev[idx];
        setArchivedAgendas((arch) => [
          {
            id: a.id,
            title: a.title,
            amount: a.amount,
            date: 'Dilewati hari ini',
            accountLabel: a.accountLabel,
            status: 'Dilewati',
          },
          ...arch,
        ]);
        pushToast(`Agenda "${a.title}" telah dilewati.`, 'info');
        return prev.filter((x) => x.id !== id);
      });
    },
    [pushToast]
  );

  const postponeAgenda = useCallback(
    (id: string) => {
      setAgendas((prev) =>
        prev.map((a) =>
          a.id === id ? { ...a, displayDate: 'Besok (Ditunda)' } : a
        )
      );
      const a = agendas.find((x) => x.id === id);
      if (a) pushToast(`Agenda "${a.title}" ditunda ke besok.`);
    },
    [agendas, pushToast]
  );

  const deleteAgenda = useCallback(
    (id: string) => {
      setAgendas((prev) => {
        const a = prev.find((x) => x.id === id);
        if (a) pushToast(`Agenda "${a.title}" telah dihapus.`);
        return prev.filter((x) => x.id !== id);
      });
    },
    [pushToast]
  );

  const finishAgenda = useCallback(
    (id: string) => {
      setAgendas((prev) => {
        const idx = prev.findIndex((a) => a.id === id);
        if (idx === -1) return prev;
        const a = prev[idx];
        setArchivedAgendas((arch) => [
          {
            id: a.id,
            title: a.title,
            amount: a.amount,
            date: a.isIncome ? 'Diterima hari ini' : 'Terbayar hari ini',
            accountLabel: a.accountLabel,
            status: 'Selesai',
          },
          ...arch,
        ]);
        pushToast(`Agenda "${a.title}" berhasil diselesaikan!`, 'task_alt');
        return prev.filter((x) => x.id !== id);
      });
    },
    [pushToast]
  );

  const createAgenda = useCallback(
    (input: CreateAgendaInput) => {
      setAgendas((prev) => [
        ...prev,
        {
          id: `agd-${Date.now()}`,
          title: input.title,
          amount: input.amount,
          isIncome: input.isIncome,
          displayDate: formatDateDisplay(input.rawDate),
          rawDate: input.rawDate,
          accountLabel: input.accountLabel,
          categoryLabel: input.categoryLabel,
          repeat: input.repeat,
          type: input.type,
          note: input.note,
          icon: input.icon || (input.isIncome ? 'payments' : 'receipt_long'),
        },
      ]);
      pushToast(`Agenda "${input.title}" berhasil disimpan.`);
    },
    [pushToast]
  );

  const posArchivedCount = posItems.filter((p) => p.archived).length;

  return {
    derived,
    accounts,
    billsDue,
    posItems,
    posArchivedCount,
    agendas,
    archivedAgendas,
    transactions,
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

    skipAgenda,
    postponeAgenda,
    deleteAgenda,
    finishAgenda,
    createAgenda,
  };
}

export { POS_PER_PAGE, AGENDA_PER_PAGE, ACTIVITY_PER_PAGE };
