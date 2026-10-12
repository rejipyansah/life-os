import { useCallback, useEffect, useMemo, useState } from 'react';

import { ApiRequestError, getFinanceState } from '../api';
import type { FinanceStateProjection, SetAsideCycleKind } from '../types';
import type { FinanceDerived } from './financeCalc';
import { mapFinanceState } from './apiMapping';
import {
  createAccountActions,
  createAgendaActions,
  createBillActions,
  createSetAsideActions,
  createTransactionActions,
} from './financeActions';
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

function errorMessage(error: unknown): string {
  if (error instanceof ApiRequestError) return error.message;
  if (error instanceof TypeError)
    return 'Tidak dapat terhubung ke server. Periksa koneksi internet lalu coba lagi.';
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
  isLoading: boolean;
  loadError: string | null;
  retry: () => void;

  posFilter: PosCategory | 'all';
  posPage: number;
  agendaFilter: 'all' | 'scheduled' | 'flexible';
  agendaPage: number;
  activityFilter: TimePeriod | 'all';
  activityPage: number;

  setPosFilter: (filter: PosCategory | 'all') => void;
  setPosPage: (page: number) => void;
  setAgendaFilter: (filter: 'all' | 'scheduled' | 'flexible') => void;
  setAgendaPage: (page: number) => void;
  setActivityFilter: (filter: TimePeriod | 'all') => void;
  setActivityPage: (page: number) => void;

  dismissToast: (id: number) => void;
  pushToast: (message: string, icon?: string) => void;

  payBill: (id: string, accountId: string, setAsideId?: string) => Promise<boolean>;
  payAllBills: (accountId: string, setAsideId?: string) => Promise<boolean>;
  postponeBill: (id: string) => void;

  addAccount: (data: { name: string; type: Account['type'] }) => Promise<boolean>;
  updateAccount: (id: string, data: { name: string; type: Account['type'] }) => Promise<boolean>;
  deleteAccount: (id: string) => Promise<boolean>;
  transfer: (fromId: string, toId: string, amount: number) => Promise<boolean>;

  saveTransaction: (parsed: ParsedTransaction) => Promise<boolean>;
  voidTransaction: (id: string, reason: string) => void;

  topUpPos: (id: string, amount: number, sourceAccountId?: string) => Promise<boolean>;
  withdrawPos: (id: string, amount: number) => Promise<boolean>;
  useIncrementalPos: (id: string, amount: number, sourceAccountId: string, note?: string) => Promise<boolean>;
  executeBatchPos: (id: string, actualCost: number, sourceAccountId: string, note?: string) => Promise<boolean>;
  executeSingleSpendPos: (id: string, actualCost: number, sourceAccountId: string, note?: string) => Promise<boolean>;
  createPos: (input: CreatePosInput) => Promise<boolean>;
  deletePos: (id: string) => Promise<boolean>;
  completePos: (id: string) => Promise<boolean>;
  updatePos: (id: string, command: {
    name: string;
    note: string;
    transactionCategory: string;
    targetAmount?: number;
    removeTarget?: boolean;
    cycleKind: SetAsideCycleKind;
  }) => Promise<boolean>;

  skipAgenda: (id: string) => void;
  scheduleFlexibleAgenda: (id: string, newDueDate: string) => Promise<boolean>;
  cancelAgenda: (id: string) => void;
  finishAgenda: (id: string, accountId: string) => Promise<boolean>;
  createAgenda: (input: CreateAgendaInput) => Promise<boolean>;
  updateAgenda: (id: string, input: CreateAgendaInput) => Promise<boolean>;
}

export function useFinanceState(): FinanceStateApi {
  const [projection, setProjection] = useState<FinanceStateProjection | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [completedReloadKey, setCompletedReloadKey] = useState<number | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [posFilter, setPosFilter] = useState<PosCategory | 'all'>('all');
  const [posPage, setPosPage] = useState(1);
  const [agendaFilter, setAgendaFilter] = useState<'all' | 'scheduled' | 'flexible'>('all');
  const [agendaPage, setAgendaPage] = useState(1);
  const [activityFilter, setActivityFilter] = useState<TimePeriod | 'all'>('all');
  const [activityPage, setActivityPage] = useState(1);
  const [toasts, setToasts] = useState<FinanceToast[]>([]);

  // Financial figures remain server-owned; the hook only loads and maps the read model.
  useEffect(() => {
    let cancelled = false;
    getFinanceState()
      .then((next) => {
        if (!cancelled) {
          setProjection(next);
          setLoadError(null);
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) setLoadError(errorMessage(error));
      })
      .finally(() => {
        if (!cancelled) setCompletedReloadKey(reloadKey);
      });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const isLoading = completedReloadKey !== reloadKey;
  const mapped = useMemo(() => mapFinanceState(projection), [projection]);
  const refresh = useCallback(() => setReloadKey((key) => key + 1), []);

  const pushToast = useCallback((message: string, icon?: string) => {
    const id = ++toastSeq;
    setToasts((previous) => [...previous, { id, message, icon }]);
    window.setTimeout(() => {
      setToasts((previous) => previous.filter((toast) => toast.id !== id));
    }, 3200);
  }, []);

  const dismissToast = useCallback((id: number) => {
    setToasts((previous) => previous.filter((toast) => toast.id !== id));
  }, []);

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
    [pushToast, refresh],
  );

  const resolveAccountId = useCallback(
    (name: string | undefined): string | undefined => {
      if (!name) return undefined;
      const wanted = name.trim().toLowerCase();
      return mapped.accounts.find((account) => account.name.toLowerCase() === wanted)?.id;
    },
    [mapped.accounts],
  );

  const mutationSupport = useMemo(() => ({ run, pushToast }), [run, pushToast]);
  const billActions = useMemo(
    () => createBillActions(mapped.billsDue, mutationSupport, refresh),
    [mapped.billsDue, mutationSupport, refresh],
  );
  const accountActions = useMemo(
    () => createAccountActions(mapped.accounts, mutationSupport),
    [mapped.accounts, mutationSupport],
  );
  const transactionActions = useMemo(
    () => createTransactionActions(resolveAccountId, mutationSupport),
    [resolveAccountId, mutationSupport],
  );
  const setAsideActions = useMemo(
    () => createSetAsideActions(mapped.posItems, resolveAccountId, mutationSupport),
    [mapped.posItems, resolveAccountId, mutationSupport],
  );
  const agendaActions = useMemo(
    () => createAgendaActions(mapped.agendas, mutationSupport),
    [mapped.agendas, mutationSupport],
  );

  return {
    ...billActions,
    ...accountActions,
    ...transactionActions,
    ...setAsideActions,
    ...agendaActions,

    derived: mapped.derived,
    accounts: mapped.accounts,
    billsDue: mapped.billsDue,
    posItems: mapped.posItems,
    posArchivedCount: mapped.posArchivedCount,
    agendas: mapped.agendas,
    archivedAgendas: mapped.archivedAgendas,
    transactions: mapped.transactions,
    toasts,
    isLoading,
    loadError,
    retry: refresh,

    posFilter,
    posPage,
    agendaFilter,
    agendaPage,
    activityFilter,
    activityPage,

    setPosFilter: (filter) => {
      setPosFilter(filter);
      setPosPage(1);
    },
    setPosPage,
    setAgendaFilter: (filter) => {
      setAgendaFilter(filter);
      setAgendaPage(1);
    },
    setAgendaPage,
    setActivityFilter: (filter) => {
      setActivityFilter(filter);
      setActivityPage(1);
    },
    setActivityPage,

    dismissToast,
    pushToast,
  };
}

export { POS_PER_PAGE, AGENDA_PER_PAGE, ACTIVITY_PER_PAGE };
