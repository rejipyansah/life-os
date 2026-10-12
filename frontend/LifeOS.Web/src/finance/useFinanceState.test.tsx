// @vitest-environment jsdom

import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ApiRequestError, getFinanceState } from '../api';
import type { FinanceStateProjection } from '../types';
import { useFinanceState } from './useFinanceState';

vi.mock('../api', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api')>();
  return { ...original, getFinanceState: vi.fn() };
});

const emptyFinanceState: FinanceStateProjection = {
  today: '2026-10-12',
  totalActualBalance: 0,
  totalSetAside: 0,
  pendingCycleFunding: 0,
  pendingCycleSurplus: 0,
  totalCommittedSetAside: 0,
  totalAvailable: 0,
  dueObligations: 0,
  dueObligationsCount: 0,
  overdueObligationsCount: 0,
  scheduledExpenseCommitments: 0,
  freeCash: 0,
  hasUnpaidBills: false,
  allBillsPaid: false,
  accounts: [],
  setAsides: [],
  upcomingEvents: [],
  archivedEvents: [],
  dueEvents: [],
  recentTransactions: [],
};

describe('useFinanceState loading state', () => {
  afterEach(() => vi.clearAllMocks());

  it('exposes a recoverable loading error and clears it after retry succeeds', async () => {
    const request = vi.mocked(getFinanceState);
    request.mockRejectedValueOnce(new ApiRequestError('Data belum dapat dimuat.', 503, 'service_unavailable'));
    request.mockResolvedValueOnce(emptyFinanceState);

    const { result } = renderHook(() => useFinanceState());
    await waitFor(() => expect(result.current.loadError).toBe('Data belum dapat dimuat.'));
    expect(result.current.isLoading).toBe(false);

    act(() => result.current.retry());
    expect(result.current.isLoading).toBe(true);
    await waitFor(() => expect(result.current.loadError).toBeNull());
    expect(result.current.derived.freeCash).toBe(0);
    expect(result.current.isLoading).toBe(false);
  });
});
