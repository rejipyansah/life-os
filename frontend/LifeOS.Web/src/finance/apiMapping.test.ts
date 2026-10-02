import { describe, it, expect } from 'vitest';
import type {
  AccountStateProjection,
  FinanceStateProjection,
  SetAsideProjection,
  TransactionProjection,
  UpcomingEventProjection,
} from '../types';
import type { CreatePosInput } from './types';
import {
  mapAccount,
  mapArchivedAgenda,
  mapBillDue,
  mapFinanceState,
  mapPosItem,
  mapTransaction,
  signedAmountFor,
  toAccountType,
  toCreateSetAsideCommand,
  toPosCategory,
} from './apiMapping';

function account(overrides: Partial<AccountStateProjection> = {}): AccountStateProjection {
  return {
    id: 'acc-1',
    name: 'SeaBank',
    type: 'Bank',
    isArchived: false,
    actualBalance: 2_000_000,
    setAsideAmount: 500_000,
    availableBalance: 1_500_000,
    pendingCycleFunding: 0,
    pendingCycleSurplus: 0,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function setAside(overrides: Partial<SetAsideProjection> = {}): SetAsideProjection {
  return {
    id: 'pos-1',
    accountId: 'acc-1',
    accountName: 'SeaBank',
    name: 'Dana Makan',
    kind: 'RoutineIncremental',
    note: null,
    amount: 200_000,
    targetAmount: 300_000,
    targetShortfall: 100_000,
    cycleKind: 'Monthly',
    cycleAnchorDate: '2026-09-01',
    currentCycleStart: '2026-09-01',
    currentCycleEnd: '2026-09-30',
    isCycleRolloverPending: false,
    cycleFundingRequired: 0,
    cycleSurplus: 0,
    cycleFundingShortfall: 0,
    isUnderfunded: false,
    usedAmount: 100_000,
    status: 'Active',
    closeReason: null,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    recentEntries: [],
    ...overrides,
  };
}

function event(overrides: Partial<UpcomingEventProjection> = {}): UpcomingEventProjection {
  return {
    id: 'evt-1',
    accountId: 'acc-1',
    accountName: 'Mandiri',
    title: 'WiFi Rumah',
    amount: 340_440,
    direction: 'Expense',
    categoryName: 'Rutin',
    note: null,
    dueDate: '2026-10-01',
    scheduleKind: 'Scheduled',
    recurrence: 'None',
    status: 'Scheduled',
    realizedTransactionId: null,
    statusReason: null,
    isDue: true,
    isOverdue: false,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

function transaction(overrides: Partial<TransactionProjection> = {}): TransactionProjection {
  return {
    id: 'tx-1',
    type: 'Expense',
    amount: 250_000,
    description: 'Salah catat',
    categoryName: 'Rutin',
    occurredOn: '2026-09-30',
    createdAt: '2026-09-30T07:15:00Z',
    relatedTransactionId: null,
    feeAmount: null,
    entries: [{ accountId: 'acc-1', accountName: 'SeaBank', amount: -250_000 }],
    isReversed: false,
    reversalReason: null,
    ...overrides,
  };
}

function projection(overrides: Partial<FinanceStateProjection> = {}): FinanceStateProjection {
  return {
    today: '2026-09-30',
    totalActualBalance: 2_000_000,
    totalSetAside: 500_000,
    pendingCycleFunding: 100_000,
    pendingCycleSurplus: 0,
    totalCommittedSetAside: 600_000,
    totalAvailable: 1_500_000,
    dueObligations: 340_440,
    dueObligationsCount: 1,
    overdueObligationsCount: 0,
    scheduledExpenseCommitments: 340_440,
    freeCash: 1_059_560,
    hasUnpaidBills: true,
    allBillsPaid: false,
    accounts: [account()],
    setAsides: [setAside()],
    upcomingEvents: [event()],
    dueEvents: [event()],
    recentTransactions: [transaction()],
    ...overrides,
  };
}

describe('mapFinanceState', () => {
  it('returns an empty state without a projection', () => {
    const result = mapFinanceState(null);
    expect(result.accounts).toEqual([]);
    expect(result.transactions).toEqual([]);
    expect(result.derived.freeCash).toBe(0);
  });

  it('reads every derived figure straight from the backend projection', () => {
    const result = mapFinanceState(projection());

    // The client must not recompute Uang Bebas — it is whatever the backend says.
    expect(result.derived.freeCash).toBe(1_059_560);
    expect(result.derived.totalLiquidity).toBe(2_000_000);
    expect(result.derived.savingsCommitment).toBe(600_000);
    expect(result.derived.billsDueTotal).toBe(340_440);
    expect(result.derived.scheduledExpenseCommitments).toBe(340_440);
    expect(result.derived.unpaidBillsCount).toBe(1);
    expect(result.derived.commitmentTotal).toBe(600_000 + 340_440);
    expect(result.derived.hasUnpaidBills).toBe(true);
    expect(result.derived.allBillsPaid).toBe(false);
  });

  it('maps planned expense commitments even when not yet due', () => {
    const result = mapFinanceState(
      projection({
        dueObligations: 0,
        dueObligationsCount: 0,
        scheduledExpenseCommitments: 350_000,
        freeCash: 1_050_000,
        hasUnpaidBills: false,
        dueEvents: [],
      })
    );

    expect(result.derived.scheduledExpenseCommitments).toBe(350_000);
    expect(result.derived.billsDueTotal).toBe(0);
    expect(result.derived.unpaidBillsCount).toBe(0);
    expect(result.derived.commitmentTotal).toBe(600_000 + 350_000);
    expect(result.derived.freeCash).toBe(1_050_000);
  });

  it('never clamps a negative free cash from the backend', () => {
    const result = mapFinanceState(projection({ freeCash: -400_000 }));
    expect(result.derived.freeCash).toBe(-400_000);
  });

  it('splits active and archived set-asides', () => {
    const result = mapFinanceState(
      projection({
        setAsides: [
          setAside({ id: 'a', status: 'Active' }),
          setAside({ id: 'b', name: 'Tabungan', status: 'Closed' }),
        ],
      })
    );

    expect(result.posItems).toHaveLength(2);
    expect(result.posArchivedCount).toBe(1);
    expect(result.posItems.find((p) => p.id === 'b')?.archived).toBe(true);
  });

  it('splits scheduled agendas from the archive', () => {
    const result = mapFinanceState(
      projection({
        upcomingEvents: [
          event({ id: 'live', status: 'Scheduled' }),
          event({ id: 'done', status: 'Realized', dueDate: '2026-09-20' }),
          event({ id: 'skipped', status: 'Skipped', dueDate: '2026-09-21' }),
        ],
        dueEvents: [],
      })
    );

    expect(result.agendas.map((a) => a.id)).toEqual(['live']);
    expect(result.archivedAgendas.map((a) => a.id)).toEqual(['done', 'skipped']);
    expect(result.archivedAgendas.find((a) => a.id === 'done')?.status).toBe('Selesai');
    expect(result.archivedAgendas.find((a) => a.id === 'skipped')?.status).toBe('Dilewati');
  });

  it('maps upcoming income events as income and leaves expense as expense', () => {
    const result = mapFinanceState(
      projection({
        upcomingEvents: [
          event({ id: 'gaji', direction: 'Income', title: 'Gaji', isDue: false }),
          event({ id: 'wifi', direction: 'Expense' }),
        ],
        dueEvents: [],
      })
    );

    expect(result.agendas.find((a) => a.id === 'gaji')?.isIncome).toBe(true);
    expect(result.agendas.find((a) => a.id === 'wifi')?.isIncome).toBe(false);
    expect(result.agendas.find((a) => a.id === 'wifi')?.displayDate).toBe('1 Okt 2026');
  });
});

describe('mapAccount', () => {
  it('maps the backend actual balance, available balance and type onto the UI view-model', () => {
    const mapped = mapAccount({
      id: 'a',
      name: 'GoPay',
      type: 'EWallet',
      isArchived: false,
      actualBalance: 100_000,
      setAsideAmount: 25_000,
      availableBalance: 75_000,
      createdAt: '2026-01-01T00:00:00Z',
    });

    expect(mapped.balance).toBe(100_000);
    expect(mapped.availableBalance).toBe(75_000);
    expect(mapped.type).toBe('ewallet');
    expect(mapped.icon).toBe('account_balance_wallet');
  });

  it('maps every backend account type', () => {
    expect(toAccountType('Cash')).toBe('cash');
    expect(toAccountType('Bank')).toBe('bank');
    expect(toAccountType('EWallet')).toBe('ewallet');
    expect(toAccountType('Credit')).toBe('credit');
  });
});

describe('mapPosItem', () => {
  it('maps set-aside kind, target and cycle onto the pos view-model', () => {
    const mapped = mapPosItem(setAside());

    expect(mapped.category).toBe('routine_incremental');
    expect(mapped.amount).toBe(200_000);
    expect(mapped.targetAmount).toBe(300_000);
    // A cycling set-aside's plafon is its per-cycle target balance.
    expect(mapped.plafon).toBe(300_000);
    expect(mapped.usedAmount).toBe(100_000);
    expect(mapped.cycle).toBe('Bulanan');
    expect(mapped.accountLabel).toBe('SeaBank');
    expect(mapped.archived).toBe(false);
  });

  it('does not invent a plafon for a set-aside without a cycle', () => {
    const mapped = mapPosItem(
      setAside({ cycleKind: 'None', currentCycleStart: null, currentCycleEnd: null })
    );

    expect(mapped.plafon).toBeUndefined();
    expect(mapped.cycle).toBeUndefined();
    expect(mapped.targetAmount).toBe(300_000);
  });

  it('surfaces an underfunded set-aside instead of hiding it', () => {
    const mapped = mapPosItem(setAside({ isUnderfunded: true }));
    expect(mapped.status).toBe('Kurang pendanaan');
  });

  it('maps every backend kind onto a pos category', () => {
    expect(toPosCategory('Saving')).toBe('saving');
    expect(toPosCategory('RoutineIncremental')).toBe('routine_incremental');
    expect(toPosCategory('RoutineBatch')).toBe('routine_batch');
    expect(toPosCategory('SingleSpend')).toBe('single_spend');
    // Legacy rows carry no kind.
    expect(toPosCategory(null)).toBe('saving');
  });
});

describe('toCreateSetAsideCommand', () => {
  function posInput(overrides: Partial<CreatePosInput> = {}): CreatePosInput {
    return {
      name: 'Dana Makan',
      description: 'Pos dana baru',
      category: 'routine_incremental',
      accountLabel: 'SeaBank',
      plafon: 300_000,
      ...overrides,
    };
  }

  it('auto-funds Rutinitas Bertahap with the plafon amount on create', () => {
    const command = toCreateSetAsideCommand(
      posInput({ cycleKind: 'Monthly' }),
      'acc-1'
    );

    expect(command.accountId).toBe('acc-1');
    expect(command.name).toBe('Dana Makan');
    expect(command.kind).toBe('RoutineIncremental');
    expect(command.targetAmount).toBe(300_000);
    expect(command.cycleKind).toBe('Monthly');
    // Pendanaan awal bukan input user — sistem menyiapkan dana = plafon.
    expect(command.amount).toBe(300_000);
  });

  it('auto-funds Rutinitas Berkala with the estimasi biaya amount on create', () => {
    const command = toCreateSetAsideCommand(
      posInput({
        category: 'routine_batch',
        plafon: 500_000,
        cycle: 'Per 3 Bulan',
        cycleKind: undefined,
      }),
      'acc-1'
    );

    expect(command.kind).toBe('RoutineBatch');
    expect(command.targetAmount).toBe(500_000);
    expect(command.cycleKind).toBe('Quarterly');
    // Dana langsung disisihkan dari Uang Bebas saat pos dibuat.
    expect(command.amount).toBe(500_000);
  });

  it('auto-funds Sekali Pakai with the target anggaran amount on create', () => {
    const command = toCreateSetAsideCommand(
      posInput({
        category: 'single_spend',
        plafon: 2_000_000,
        cycleKind: undefined,
      }),
      'acc-1'
    );

    expect(command.kind).toBe('SingleSpend');
    expect(command.targetAmount).toBe(2_000_000);
    expect(command.cycleKind).toBe('None');
    expect(command.amount).toBe(2_000_000);
  });

  it('does not auto-fund Tabungan on create — saldo awal tetap 0', () => {
    const command = toCreateSetAsideCommand(
      posInput({ category: 'saving', targetAmount: 500_000, plafon: undefined }),
      'acc-1'
    );

    expect(command.kind).toBe('Saving');
    expect(command.targetAmount).toBe(500_000);
    expect(command.cycleKind).toBe('None');
    expect(command.amount).toBe(0);
  });

  it('ignores an explicit amount for auto-fund categories', () => {
    const command = toCreateSetAsideCommand(posInput({ amount: 99_000 }), 'acc-1');

    expect(command.amount).toBe(300_000);
  });

  it('sends CycleKind = None for routine_incremental without a cycle selection', () => {
    const command = toCreateSetAsideCommand(posInput({ cycleKind: undefined }), 'acc-1');

    expect(command.kind).toBe('RoutineIncremental');
    expect(command.cycleKind).toBe('None');
  });

  it('still resolves routine_batch Indonesian cycle labels via fromCycleLabel', () => {
    const command = toCreateSetAsideCommand(
      posInput({
        category: 'routine_batch',
        plafon: 500_000,
        cycle: 'Per 3 Bulan',
        cycleKind: undefined,
      }),
      'acc-1'
    );

    expect(command.kind).toBe('RoutineBatch');
    expect(command.targetAmount).toBe(500_000);
    expect(command.cycleKind).toBe('Quarterly');
  });

  it('forces CycleKind = None for routine_batch when plafon/target is missing', () => {
    const command = toCreateSetAsideCommand(
      posInput({
        category: 'routine_batch',
        plafon: undefined,
        cycle: 'Bulanan',
        cycleKind: undefined,
      }),
      'acc-1'
    );

    expect(command.cycleKind).toBe('None');
  });

  it('maps single_spend onto SingleSpend with no cycle', () => {
    const command = toCreateSetAsideCommand(
      posInput({
        category: 'single_spend',
        plafon: 400_000,
        cycleKind: undefined,
      }),
      'acc-1'
    );

    expect(command.kind).toBe('SingleSpend');
    expect(command.targetAmount).toBe(400_000);
    expect(command.cycleKind).toBe('None');
  });
});

describe('mapBillDue', () => {
  it('renders a due event as an unpaid bill', () => {
    const mapped = mapBillDue(event());
    expect(mapped.id).toBe('evt-1');
    expect(mapped.name).toBe('WiFi Rumah');
    expect(mapped.amount).toBe(340_440);
    expect(mapped.status).toBe('unpaid');
    expect(mapped.sourceAccountId).toBe('acc-1');
  });

  it('flags an overdue bill', () => {
    const mapped = mapBillDue(event({ isOverdue: true }));
    expect(mapped.badge).toBe('Terlambat');
    expect(mapped.icon).toBe('bolt');
  });
});

describe('mapTransaction', () => {
  it('signs an expense as money leaving the account', () => {
    const mapped = mapTransaction(transaction(), undefined);
    expect(mapped.amount).toBe(-250_000);
    expect(mapped.isVoided).toBe(false);
    expect(mapped.isReversal).toBe(false);
  });

  it('signs income as money entering the account', () => {
    const mapped = mapTransaction(transaction({ type: 'Income' }), undefined);
    expect(mapped.amount).toBe(250_000);
  });

  it('labels a transfer as a movement, never as an expense', () => {
    const mapped = mapTransaction(
      transaction({ type: 'Transfer', description: 'Ke SeaBank', categoryName: null }),
      undefined
    );
    expect(mapped.amount).toBe(250_000);
    expect(mapped.title).toBe('Ke SeaBank');
    expect(mapped.category).toBe('Transfer');
    expect(mapped.icon).toBe('sync_alt');
  });

  it('marks a reversed transaction and carries the reason', () => {
    const mapped = mapTransaction(
      transaction({ isReversed: true, reversalReason: 'Salah catat / nominal typo' }),
      undefined
    );
    expect(mapped.isVoided).toBe(true);
    expect(mapped.voidReason).toBe('Salah catat / nominal typo');
  });

  it('signs a reversal from the transaction it corrects', () => {
    // Reversing an expense credits money back.
    expect(
      signedAmountFor(transaction({ type: 'Reversal' }), 'Expense')
    ).toBe(250_000);
    // Reversing an income takes money away.
    expect(
      signedAmountFor(transaction({ type: 'Reversal' }), 'Income')
    ).toBe(-250_000);
  });

  it('marks a reversal row as a reversal', () => {
    const mapped = mapTransaction(transaction({ type: 'Reversal' }), 'Expense');
    expect(mapped.isReversal).toBe(true);
  });
});

describe('mapArchivedAgenda', () => {
  it('renders a realized event with its archive status', () => {
    const mapped = mapArchivedAgenda(
      event({ status: 'Realized', dueDate: '2026-09-20' })
    );
    expect(mapped.status).toBe('Selesai');
    expect(mapped.date).toBe('20 Sep 2026');
  });
});
