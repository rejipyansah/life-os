import { useMemo, useState } from 'react';

import {
  formatCurrency,
  formatCurrencyRaw,
  type Account,
  type BillDue,
  type FinanceDerived,
  type PosItem,
} from '../../finance';
import {
  btnPrimary,
  cardBase,
  Icon,
  Modal,
  moneyClass,
  pillWarning,
  selectBase,
} from './shared';

/* ── Uang Bebas card ── */

export function UangBebasCard({
  derived,
  onOpenFormula,
}: {
  derived: FinanceDerived;
  onOpenFormula: () => void;
}) {
  return (
    <div className={`${cardBase} h-full p-7 sm:p-8 flex flex-col justify-between`}>
      <div className="space-y-6">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-lo-secondary shrink-0" />
            <span className="text-[11px] uppercase tracking-wider font-semibold text-lo-text-subtle">
              Uang Bebas Hari Ini
            </span>
          </div>
          <span className="text-xs font-medium text-lo-secondary bg-lo-accent-wash px-3 py-1 rounded-full border border-lo-border-hairline">
            Batas Aman Dibelanjakan
          </span>
        </div>
        <div className="space-y-3 pt-2">
          <h1 className={`${moneyClass} text-4xl sm:text-[46px] font-medium leading-none select-all`}>
            {formatCurrencyRaw(derived.freeCash)}
          </h1>
          <p className="text-xs text-lo-text-subtle leading-relaxed">
            Jumlah yang aman dibelanjakan hari ini tanpa mengganggu tabungan maupun tagihan.
          </p>
        </div>
      </div>
      <div className="pt-6 mt-6 border-t border-lo-border-hairline flex items-center justify-between">
        <button
          type="button"
          onClick={onOpenFormula}
          className="inline-flex items-center gap-2 text-xs font-medium text-lo-text-subtle hover:text-lo-secondary transition-colors cursor-pointer group"
        >
          <Icon
            name="calculate"
            className="text-[17px] text-lo-secondary group-hover:scale-110 transition-transform"
          />
          <span className="underline underline-offset-4 decoration-lo-outline-variant group-hover:decoration-lo-secondary">
            Lihat Cara Menghitung
          </span>
        </button>
        <span className="text-[11px] text-lo-text-subtle tabular-nums">
          Diperbarui otomatis
        </span>
      </div>
    </div>
  );
}

/* ── Realize modal (Bayar / Bayar Semua) ──
 * Sumber Dana = rekening tempat uang BENAR-BENAR keluar (wajib).
 * Dana yang Disisihkan = pos yang dialokasikan/dilepas (opsional).
 * Tagihan tidak lagi terikat akun — user memilih di modal ini. */

type PayModalState =
  | { kind: 'single'; bill: BillDue }
  | { kind: 'all' }
  | null;

function PayRealizeModal({
  state,
  accounts,
  posItems,
  totalAmount,
  onClose,
  onConfirm,
}: {
  state: PayModalState;
  accounts: Account[];
  posItems: PosItem[];
  totalAmount: number;
  onClose: () => void;
  onConfirm: (accountId: string, setAsideId?: string) => void;
}) {
  const [accountId, setAccountId] = useState('');
  const [setAsideId, setSetAsideId] = useState('');

  const activeAccounts = useMemo(() => accounts.filter((a) => !a.archived), [accounts]);
  const activePos = useMemo(() => posItems.filter((p) => !p.archived), [posItems]);

  if (!state) return null;

  const isSingle = state.kind === 'single';
  const bill = isSingle ? state.bill : null;
  const amount = bill ? bill.amount : totalAmount;
  const title = isSingle ? `Bayar Tagihan — ${bill?.name ?? ''}` : 'Bayar Semua Tagihan';

  return (
    <Modal
      open
      onClose={onClose}
      title={title}
      subtitle="Pilih Sumber Dana tempat uang keluar; pos bersifat opsional."
      maxWidth="max-w-md"
      footer={
        <>
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink transition-colors cursor-pointer"
          >
            Batal
          </button>
          <button
            type="button"
            disabled={!accountId}
            onClick={() => onConfirm(accountId, setAsideId || undefined)}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="check" className="text-base" />
            <span>{state.kind === 'single' ? 'Bayar Tagihan' : 'Bayar Semua'}</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="pay-source-account">
            Sumber Dana <span className="text-lo-secondary">*</span>
          </label>
          <span className="text-[11px] text-lo-text-subtle">
            Uang keluar dari rekening ini.
          </span>
          <select
            id="pay-source-account"
            className={selectBase}
            value={accountId}
            onChange={(e) => setAccountId(e.target.value)}
          >
            <option value="" disabled>
              Pilih Sumber Dana…
            </option>
            {activeAccounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name} — {formatCurrencyRaw(a.availableBalance)} tersedia
              </option>
            ))}
          </select>
          {activeAccounts.length === 0 ? (
            <p className="text-[11px] text-lo-error">
              Belum ada sumber dana aktif. Tambahkan sumber dana di bagian Sumber Dana.
            </p>
          ) : null}
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="pay-set-aside">
            Dana yang Disisihkan (opsional)
          </label>
          <span className="text-[11px] text-lo-text-subtle">
            Pos yang dialokasikan/dilepas untuk pembayaran ini.
          </span>
          <select
            id="pay-set-aside"
            className={selectBase}
            value={setAsideId}
            onChange={(e) => setSetAsideId(e.target.value)}
          >
            <option value="">Tanpa pos</option>
            {activePos.map((p) => (
              <option
                key={p.id}
                value={p.id}
                disabled={p.category === 'routine_batch' && p.cycleExecuted}
              >
                {p.name} — {formatCurrencyRaw(p.amount)}
              </option>
            ))}
          </select>
        </div>

        <div className="p-4 rounded-xl bg-lo-surface-recessed border border-lo-border-hairline flex items-center justify-between">
          <span className="text-[11px] text-lo-text-subtle uppercase tracking-wide">
            {state.kind === 'single' ? 'Nominal Tagihan' : 'Total Tagihan Hari Ini'}
          </span>
          <span className="font-headline text-lg font-semibold tabular-nums text-lo-text-ink">
            {formatCurrencyRaw(amount)}
          </span>
        </div>
      </div>
    </Modal>
  );
}

/* ── Jatuh Tempo card ── */

export function JatuhTempoCard({
  derived,
  billsDue,
  accounts,
  posItems,
  onPayBill,
  onPayAll,
  onPostponeBill,
}: {
  derived: FinanceDerived;
  billsDue: BillDue[];
  accounts: Account[];
  posItems: PosItem[];
  onPayBill: (id: string, accountId: string, setAsideId?: string) => void;
  onPayAll: (accountId: string, setAsideId?: string) => void;
  onPostponeBill: (id: string) => void;
}) {
  const [payModal, setPayModal] = useState<PayModalState>(null);

  return (
    <div className={`${cardBase} h-full p-7 sm:p-8 flex flex-col justify-between`}>
      <div>
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-5 pb-4 border-b border-lo-border-hairline">
          <div>
            <div className="flex items-center gap-2 mb-1">
              <span className="w-2 h-2 rounded-full bg-lo-warning shrink-0" />
              <span className="text-[11px] uppercase tracking-wider font-semibold text-lo-text-ink">
                Jatuh Tempo Hari Ini
              </span>
            </div>
            <p className="text-xs text-lo-text-subtle font-medium">
              {derived.allBillsPaid ? (
                <>
                  <span className="text-lo-text-ink font-semibold">Semua Tagihan</span> Lunas
                  · Total{' '}
                  <span className="font-semibold text-lo-secondary tabular-nums">Rp 0</span>
                </>
              ) : (
                <>
                  <span className="text-lo-text-ink font-semibold">
                    {derived.unpaidBillsCount} Tagihan Aktif
                  </span>{' '}
                  · Total{' '}
                  <span className="font-semibold text-lo-warning tabular-nums">
                    {formatCurrencyRaw(derived.billsDueTotal)}
                  </span>
                </>
              )}
            </p>
          </div>
          <div className="shrink-0 flex items-center gap-2">
            {!derived.allBillsPaid && derived.unpaidBillsCount > 0 ? (
              <button
                type="button"
                onClick={() => setPayModal({ kind: 'all' })}
                className={btnPrimary}
              >
                <Icon name="done_all" className="text-[16px]" />
                <span>
                  Bayar Semua Sekaligus ({formatCurrencyRaw(derived.billsDueTotal)})
                </span>
              </button>
            ) : (
              <span className="inline-flex items-center gap-1.5 px-4 py-2 rounded-full bg-lo-accent-wash text-lo-secondary text-xs font-semibold">
                <Icon name="done_all" className="text-[16px]" />
                <span>Semua Tagihan Lunas</span>
              </span>
            )}
          </div>
        </div>

        <div className="space-y-3">
          {billsDue.map((bill) => (
            <BillRow
              key={bill.id}
              bill={bill}
              onPay={() => setPayModal({ kind: 'single', bill })}
              onPostpone={() => onPostponeBill(bill.id)}
            />
          ))}
          {billsDue.length === 0 ? (
            <div className="py-8 text-center text-xs text-lo-text-subtle">
              Tidak ada tagihan jatuh tempo hari ini.
            </div>
          ) : null}
        </div>
      </div>

      <div className="pt-5 mt-5 border-t border-lo-border-hairline flex flex-col sm:flex-row sm:items-center justify-between gap-2 text-xs text-lo-text-subtle">
        <div className="flex items-center gap-1.5">
          <Icon name="verified_user" className="text-sm text-lo-secondary" />
          <span>Pembayaran langsung dipotong dari Sumber Dana yang dipilih.</span>
        </div>
        <span className="text-[11px] sm:text-right">Bebas denda keterlambatan</span>
      </div>

      <PayRealizeModal
        key={
          payModal
            ? payModal.kind === 'single'
              ? `single-${payModal.bill.id}`
              : 'all'
            : 'closed'
        }
        state={payModal}
        accounts={accounts}
        posItems={posItems}
        totalAmount={derived.billsDueTotal}
        onClose={() => setPayModal(null)}
        onConfirm={(accountId, setAsideId) => {
          if (!payModal) return;
          if (payModal.kind === 'single') onPayBill(payModal.bill.id, accountId, setAsideId);
          else onPayAll(accountId, setAsideId);
        }}
      />
    </div>
  );
}

function BillRow({
  bill,
  onPay,
  onPostpone,
}: {
  bill: BillDue;
  onPay: () => void;
  onPostpone: () => void;
}) {
  const isPaid = bill.status === 'paid';
  const isPostponed = bill.status === 'postponed';

  return (
    <div
      className={`p-4 rounded-xl border transition-all duration-200 flex flex-col sm:flex-row sm:items-center justify-between gap-4 ${
        isPaid
          ? 'bg-lo-accent-wash/30 opacity-75 border-lo-border-subtle'
          : 'bg-lo-surface-cream hover:bg-lo-surface-recessed/60 border-lo-border-subtle'
      }`}
    >
      <div className="flex items-center gap-3.5 min-w-0">
        <div
          className={`w-10 h-10 rounded-full flex items-center justify-center shrink-0 border ${
            isPaid
              ? 'bg-lo-accent-wash text-lo-secondary border-lo-secondary/30'
              : bill.icon === 'bolt'
                ? 'bg-white text-lo-warning border-lo-border-hairline shadow-2xs'
                : 'bg-white text-lo-secondary border-lo-border-hairline shadow-2xs'
          }`}
        >
          <Icon name={isPaid ? 'check_circle' : bill.icon} className="text-xl" />
        </div>
        <div className="space-y-0.5 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h3 className="text-sm font-semibold text-lo-text-ink truncate">
              {bill.name}
            </h3>
            {isPaid ? (
              <span className="text-[10px] font-medium text-lo-secondary bg-lo-accent-wash px-2 py-0.5 rounded-full border border-lo-secondary/20">
                Lunas
              </span>
            ) : isPostponed ? (
              <span className="text-[10px] font-medium text-lo-text-subtle bg-lo-surface-recessed px-2 py-0.5 rounded-full border border-lo-border-hairline">
                Ditunda Besok
              </span>
            ) : (
              <span className={pillWarning}>{bill.badge}</span>
            )}
          </div>
          <p className="text-xs text-lo-text-subtle truncate">{bill.meta}</p>
        </div>
      </div>
      <div className="flex items-center justify-between sm:justify-end gap-3 pt-2 sm:pt-0 border-t sm:border-t-0 border-lo-border-subtle">
        <div className="text-left sm:text-right">
          <span
            className={`text-base font-semibold tabular-nums tracking-tight block ${
              isPaid ? 'text-lo-text-subtle line-through' : 'text-lo-text-ink'
            }`}
          >
            {formatCurrencyRaw(bill.amount)}
          </span>
          <span className="text-[10px] text-lo-text-subtle block">{bill.categoryLabel}</span>
        </div>
        {!isPaid ? (
          <div className="flex items-center gap-1.5 shrink-0">
            <button
              type="button"
              onClick={onPostpone}
              disabled={isPostponed}
              className="px-2.5 py-1.5 rounded-full hover:bg-white border border-lo-border-hairline text-lo-text-subtle hover:text-lo-text-ink text-xs font-medium transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed shrink-0"
              title="Tunda ke besok"
            >
              {isPostponed ? 'Ditunda' : 'Tunda'}
            </button>
            <button
              type="button"
              onClick={onPay}
              className="px-3 py-1.5 rounded-full bg-lo-primary hover:bg-lo-secondary text-white text-xs font-medium transition-colors cursor-pointer active:scale-95 shadow-2xs flex items-center gap-1 shrink-0"
            >
              <Icon name="check" className="text-[15px]" />
              <span>Bayar</span>
            </button>
          </div>
        ) : (
          <span className="inline-flex items-center gap-1 text-xs font-semibold text-lo-secondary px-3 py-1 bg-lo-accent-wash rounded-full">
            <Icon name="done" className="text-[15px]" />
            Selesai
          </span>
        )}
      </div>
    </div>
  );
}

/* ── Formula modal ── */

function CalcLine({
  label,
  amount,
  deduct = false,
}: {
  label: string;
  amount: number;
  deduct?: boolean;
}) {
  const showMinus = deduct && amount > 0;
  return (
    <div className="flex items-center justify-between gap-3 py-2.5">
      <span className="text-[13px] sm:text-sm font-medium text-lo-text-ink">{label}</span>
      <span className="text-[13px] sm:text-sm font-semibold tabular-nums text-lo-text-ink shrink-0">
        {showMinus ? `−${formatCurrencyRaw(amount)}` : formatCurrencyRaw(amount)}
      </span>
    </div>
  );
}

export function FormulaModal({
  open,
  onClose,
  derived,
}: {
  open: boolean;
  onClose: () => void;
  derived: FinanceDerived;
}) {
  const free = derived.freeCash;

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Kenapa uang saya yang bisa dipakai cuma segini?"
      maxWidth="max-w-lg"
      footer={
        <button
          type="button"
          onClick={onClose}
          className="w-full sm:w-auto sm:min-w-36 px-7 py-3 rounded-full bg-lo-primary hover:bg-lo-primary-hover text-white text-sm font-semibold shadow-xs transition-all cursor-pointer active:scale-95"
        >
          Oke, paham
        </button>
      }
    >
      <div className="rounded-2xl bg-white border border-lo-border-subtle p-5 sm:p-6 shadow-xs">
        <div className="divide-y divide-lo-border-hairline/70">
          <CalcLine label="Uang di sumber dana" amount={derived.totalLiquidity} />
          <CalcLine
            label="Uang yang disisihkan"
            amount={derived.savingsCommitment}
            deduct
          />
          <CalcLine
            label="Uang untuk kebutuhan mendatang"
            amount={derived.scheduledExpenseCommitments}
            deduct
          />
        </div>
        <div className="mt-4 pt-4 border-t border-lo-border-hairline flex items-center justify-between gap-3">
          <span className="text-sm font-semibold text-lo-text-ink">
            Uang yang Bisa Dipakai
          </span>
          <span className="font-headline text-lg sm:text-xl font-bold tabular-nums tracking-tight text-lo-secondary select-all">
            {formatCurrency(free)}
          </span>
        </div>
        {/* DUA ANGKA TERPISAH — totalAvailable (uang yang belum dialokasikan)
            hanya mengurangi Dana yang Disisihkan; freeCash juga memotong
            komitmen Rencana. Jangan disamakan. */}
        <p className="mt-3 text-[11px] text-lo-text-subtle leading-relaxed">
          Uang yang belum dialokasikan:{' '}
          <span className="font-medium text-lo-text-ink tabular-nums">
            {formatCurrencyRaw(derived.totalAvailable)}
          </span>{' '}
          — hanya dikurangi Dana yang Disisihkan, sebelum memotong komitmen Rencana.
        </p>
      </div>
    </Modal>
  );
}

export default function UangBebasSection({
  derived,
  billsDue,
  accounts,
  posItems,
  onPayBill,
  onPayAll,
  onPostponeBill,
}: {
  derived: FinanceDerived;
  billsDue: BillDue[];
  accounts: Account[];
  posItems: PosItem[];
  onPayBill: (id: string, accountId: string, setAsideId?: string) => void;
  onPayAll: (accountId: string, setAsideId?: string) => void;
  onPostponeBill: (id: string) => void;
}) {
  const [formulaOpen, setFormulaOpen] = useState(false);

  return (
    <>
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-stretch">
        <div className="lg:col-span-5">
          <UangBebasCard derived={derived} onOpenFormula={() => setFormulaOpen(true)} />
        </div>
        <div className="lg:col-span-7">
          <JatuhTempoCard
            derived={derived}
            billsDue={billsDue}
            accounts={accounts}
            posItems={posItems}
            onPayBill={onPayBill}
            onPayAll={onPayAll}
            onPostponeBill={onPostponeBill}
          />
        </div>
      </div>
      <FormulaModal
        open={formulaOpen}
        onClose={() => setFormulaOpen(false)}
        derived={derived}
      />
    </>
  );
}
