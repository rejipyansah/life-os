import { expect, test } from '@playwright/test';

const apiPort = process.env.LIFEOS_E2E_API_PORT ?? '5272';
const frontendPort = process.env.LIFEOS_E2E_FRONTEND_PORT ?? '5174';
const apiOrigin = `http://127.0.0.1:${apiPort}`;
const browserOrigin = `http://127.0.0.1:${frontendPort}`;
const originHeaders = { Origin: browserOrigin };
const mockApi = process.env.LIFEOS_MOBILE_PERF_MOCK === '1';

function makeMockFinanceState() {
  const now = new Date().toISOString();
  const today = now.slice(0, 10);
  return {
    today,
    totalActualBalance: 25_000_000,
    totalSetAside: 0,
    pendingCycleFunding: 0,
    pendingCycleSurplus: 0,
    totalCommittedSetAside: 0,
    totalAvailable: 25_000_000,
    dueObligations: 0,
    dueObligationsCount: 0,
    overdueObligationsCount: 0,
    scheduledExpenseCommitments: 0,
    freeCash: 25_000_000,
    hasUnpaidBills: false,
    allBillsPaid: false,
    accounts: [{
      id: 'mobile-account', name: 'Tunai', type: 'Cash', isArchived: false,
      actualBalance: 25_000_000, setAsideAmount: 0, availableBalance: 25_000_000, createdAt: now,
    }],
    setAsides: Array.from({ length: 50 }, (_, index) => ({
      id: `mobile-pos-${index}`, accountId: null, accountName: null,
      defaultSourceAccountId: null, defaultSourceAccountName: null,
      name: `Mobile sample set-aside ${index + 1}`, kind: 'Saving', note: null,
      transactionCategory: null, amount: 0, targetAmount: null, targetShortfall: 0,
      cycleKind: 'None', cycleAnchorDate: today, currentCycleStart: null, currentCycleEnd: null,
      isCycleRolloverPending: false, cycleFundingRequired: 0, cycleSurplus: 0,
      cycleFundingShortfall: 0, isUnderfunded: false, isCycleExecuted: false, usedAmount: 0,
      status: 'Active', closeReason: null, createdAt: now, updatedAt: now, recentEntries: [],
    })),
    upcomingEvents: Array.from({ length: 100 }, (_, index) => ({
      id: `mobile-agenda-${index}`, accountId: null, accountName: null,
      defaultSourceAccountId: null, defaultSourceAccountName: null,
      realizedSourceAccountId: null, realizedSourceAccountName: null,
      title: `Mobile sample agenda ${index + 1}`, amount: 10_000 + index,
      direction: 'Expense', categoryName: null, note: null,
      dueDate: new Date(Date.now() + (index + 1) * 86_400_000).toISOString().slice(0, 10),
      scheduleKind: 'Scheduled', recurrence: 'None', status: 'Scheduled',
      realizedTransactionId: null, statusReason: null, isDue: false, isOverdue: false,
      commitmentStartDate: null, isCommitmentActive: true, createdAt: now, updatedAt: now,
    })),
    archivedEvents: [],
    dueEvents: [],
    recentTransactions: [],
  };
}

test('@mobile-perf finance startup and interaction under mobile throttling', async ({ page, context }) => {
  test.setTimeout(180_000);

  const client = await context.newCDPSession(page);
  await client.send('Network.enable');
  await client.send('Network.setCacheDisabled', { cacheDisabled: true });
  await client.send('Network.emulateNetworkConditions', {
    offline: false,
    latency: 150,
    downloadThroughput: 1_600 * 1024 / 8,
    uploadThroughput: 750 * 1024 / 8,
    connectionType: 'cellular3g',
  });
  await client.send('Emulation.setCPUThrottlingRate', { rate: 4 });

  await page.addInitScript(() => {
    const metrics = { lcp: 0, cls: 0, maxEventDuration: 0 };
    Object.assign(window, { __mobilePerformanceMetrics: metrics });

    new PerformanceObserver((list) => {
      for (const entry of list.getEntries())
        metrics.lcp = Math.max(metrics.lcp, entry.startTime);
    }).observe({ type: 'largest-contentful-paint', buffered: true });

    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) {
        const shift = entry as PerformanceEntry & { hadRecentInput: boolean; value: number };
        if (!shift.hadRecentInput) metrics.cls += shift.value;
      }
    }).observe({ type: 'layout-shift', buffered: true });

    if (PerformanceObserver.supportedEntryTypes.includes('event')) {
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries())
          metrics.maxEventDuration = Math.max(metrics.maxEventDuration, (entry as PerformanceEventTiming).duration);
      }).observe({ type: 'event', buffered: true, durationThreshold: 16 });
    }
  });

  const transferredAssets = new Map<string, number>();
  page.on('response', async (response) => {
    const type = response.request().resourceType();
    if (type !== 'script' && type !== 'stylesheet' && type !== 'image') return;
    const contentLength = Number(response.headers()['content-length']);
    if (Number.isFinite(contentLength) && contentLength > 0)
      transferredAssets.set(response.url(), contentLength);
  });

  if (mockApi) {
    const financeState = makeMockFinanceState();
    await page.route('**/api/auth/me', (route) => route.fulfill({ status: 401, body: '' }));
    await page.route('**/api/guest/session', (route) => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isGuest: true }),
    }));
    await page.route('**/api/finance/state', (route) => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(financeState),
    }));
  } else {
    const session = await page.request.post(`${apiOrigin}/api/guest/session`, { headers: originHeaders });
    expect(session.ok()).toBeTruthy();

    const initialStateResponse = await page.request.get(`${apiOrigin}/api/finance/state`);
    expect(initialStateResponse.ok()).toBeTruthy();
    const initialState = await initialStateResponse.json();
    const accountId = initialState.accounts[0]?.id as string | undefined;
    expect(accountId).toBeTruthy();

    const today = new Date().toISOString().slice(0, 10);
    const income = await page.request.post(`${apiOrigin}/api/finance/transactions`, {
      headers: originHeaders,
      data: {
        type: 'Income',
        amount: 25_000_000,
        description: 'Mobile performance seed',
        occurredOn: today,
        entries: [{ accountId, amount: 25_000_000 }],
      },
    });
    expect(income.ok()).toBeTruthy();

    for (let index = 0; index < 50; index++) {
      const response = await page.request.post(`${apiOrigin}/api/finance/set-asides`, {
        headers: originHeaders,
        data: {
          name: `Mobile sample set-aside ${index + 1}`,
          kind: 'Saving',
          amount: 0,
        },
      });
      expect(response.ok()).toBeTruthy();
    }

    for (let index = 0; index < 100; index++) {
      const dueDate = new Date(Date.now() + (index + 1) * 86_400_000).toISOString().slice(0, 10);
      const response = await page.request.post(`${apiOrigin}/api/finance/upcoming-events`, {
        headers: originHeaders,
        data: {
          title: `Mobile sample agenda ${index + 1}`,
          amount: 10_000 + index,
          direction: 'Expense',
          dueDate,
          scheduleKind: 'Scheduled',
        },
      });
      expect(response.ok()).toBeTruthy();
    }
  }

  const navigationStarted = Date.now();
  let financeRequestStartedAt = 0;
  let financeResponseReceivedAt = 0;
  page.on('request', (request) => {
    if (request.url().endsWith('/api/finance/state') && request.method() === 'GET')
      financeRequestStartedAt = Date.now();
  });
  page.on('response', (response) => {
    if (response.url().endsWith('/api/finance/state') && response.request().method() === 'GET')
      financeResponseReceivedAt = Date.now();
  });
  const financeStatePromise = page.waitForResponse((response) =>
    response.url().endsWith('/api/finance/state') && response.request().method() === 'GET');
  await page.goto('/', { waitUntil: 'domcontentloaded' });
  await expect(page.getByText('Uang Bebas Hari Ini')).toBeVisible();
  const financeStateResponse = await financeStatePromise;
  const financeReadyMs = Date.now() - navigationStarted;
  const financePayloadBytes = (await financeStateResponse.body()).byteLength;
  const financeApiMs = mockApi ? null : financeResponseReceivedAt - financeRequestStartedAt;

  const interactionStartedAt = Date.now();
  await page.getByRole('button', { name: 'Keuangan' }).click();
  const syntheticTapCompletionMs = Date.now() - interactionStartedAt;
  await page.waitForTimeout(2_600);

  await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
  await page.waitForTimeout(250);
  const pageMetrics = await page.evaluate(() => {
    const navigation = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming;
    const mobile = (window as Window & {
      __mobilePerformanceMetrics: { lcp: number; cls: number; maxEventDuration: number };
    }).__mobilePerformanceMetrics;
    return {
      lcpMs: mobile.lcp,
      cls: mobile.cls,
      maxEventDurationMs: mobile.maxEventDuration,
      domContentLoadedMs: navigation.domContentLoadedEventEnd,
      loadEventMs: navigation.loadEventEnd,
      horizontalOverflowPx: document.documentElement.scrollWidth - window.innerWidth,
    };
  });

  const report = {
    profile: `Pixel 7 emulation, 4x CPU slowdown, 150 ms RTT, 1.6 Mbps down / 750 Kbps up, API ${mockApi ? 'mocked' : 'PostgreSQL-backed'}`,
    financeReadyMs,
    financeApiMs,
    financePayloadBytes,
    staticResourceContentLengthBytes: [...transferredAssets.values()].reduce((sum, size) => sum + size, 0),
    syntheticTapCompletionMs,
    seededSetAsideCount: 50,
    seededUpcomingEventCount: 100,
    ...pageMetrics,
  };
  process.stdout.write(`MOBILE_PERF_RESULT ${JSON.stringify(report)}\n`);

  expect(financeStateResponse.ok()).toBeTruthy();
  expect(pageMetrics.lcpMs, 'LCP should meet the 2.5 s mobile Core Web Vitals budget').toBeLessThanOrEqual(2_500);
  expect(pageMetrics.cls, 'CLS should meet the 0.1 mobile Core Web Vitals budget').toBeLessThanOrEqual(0.1);
  expect(pageMetrics.maxEventDurationMs, 'synthetic event duration should remain below 200 ms').toBeLessThanOrEqual(200);
  expect(financeReadyMs, 'Finance should be usable within 3 seconds in the emulated profile').toBeLessThanOrEqual(3_000);
  expect(pageMetrics.horizontalOverflowPx).toBeLessThanOrEqual(1);
});

declare global {
  interface Window {
    __mobilePerformanceMetrics: { lcp: number; cls: number; maxEventDuration: number };
  }
}
