import { expect, test } from '@playwright/test';

test('guest records income in the browser and the finance projection reflects it', async ({ page }) => {
  // Never consume Groq tokens in browser tests; exercise the real save path with a fixture interpretation.
  await page.route('**/api/interpret', (route) => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      intent: 'CreateTransaction',
      state: 'Ready',
      preview: {
        type: 'Income', amount: 750_000, description: 'Freelance desain', category: 'Pendapatan',
        account: 'Tunai', toAccount: null, date: '2026-10-13', feeAmount: null,
      },
      command: null,
      setAsidePreview: null,
      setAsideCommand: null,
      eventPreview: null,
      eventCommand: null,
      clarifications: [],
    }),
  }));

  await page.goto('/');
  await page.getByRole('button', { name: 'Coba Life OS' }).click();
  await expect(page.getByRole('heading', { name: 'Catat Transaksi' })).toBeVisible();

  await page.getByRole('button', { name: 'Tunai', exact: true }).click();
  await page.getByRole('button', { name: 'terima freelance desain 750rb' }).click();
  await page.getByRole('button', { name: 'Catat Transaksi', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Tinjau transaksi' })).toContainText('Pemasukan');
  await page.getByRole('button', { name: 'Simpan Transaksi' }).click();
  await expect(page.getByText(/Transaksi berhasil dicatat:.*Rp 750\.000/)).toBeVisible();

  await expect.poll(async () => {
    const stateResponse = await page.request.get('http://127.0.0.1:5173/api/finance/state');
    if (!stateResponse.ok()) return -1;
    const state = await stateResponse.json();
    return state.accounts.find((account: { name: string }) => account.name === 'Tunai')?.actualBalance ?? 0;
  }).toBe(750_000);

  await expect(page.getByText('Rp 750.000').first()).toBeVisible();
});
