import { useState } from 'react';

import {
  formatCurrencyRaw,
  type BillDue,
  type FinanceDerived,
} from '../../finance';
import {
  btnPrimary,
  cardBase,
  Icon,
  Modal,
  moneyClass,
  pillWarning,
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
            Nominal riil yang aman dibelanjakan hari ini tanpa mengganggu pos tabungan
            maupun tagihan.
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
            Lihat Formula Perhitungan
          </span>
        </button>
        <span className="text-[11px] text-lo-text-subtle tabular-nums">
          Diperbarui otomatis · Akurat
        </span>
      </div>
    </div>
  );
}

/* ── Jatuh Tempo card ── */

export function JatuhTempoCard({
  derived,
  billsDue,
  onPayBill,
  onPayAll,
  onPostponeBill,
}: {
  derived: FinanceDerived;
  billsDue: BillDue[];
  onPayBill: (id: string) => void;
  onPayAll: () => void;
  onPostponeBill: (id: string) => void;
}) {
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
              <button type="button" onClick={onPayAll} className={btnPrimary}>
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
              onPay={() => onPayBill(bill.id)}
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
          <span>Pembayaran langsung dipotong dari rekening sumber terpilih.</span>
        </div>
        <span className="text-[11px] sm:text-right">Bebas denda keterlambatan</span>
      </div>
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

export function FormulaModal({
  open,
  onClose,
  derived,
}: {
  open: boolean;
  onClose: () => void;
  derived: FinanceDerived;
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Kalkulasi Uang Bebas Hari Ini"
      subtitle="Formula Sadar & Hening"
      icon="calculate"
      maxWidth="max-w-lg"
    >
      <div className="space-y-6">
        <div className="grid grid-cols-3 gap-2.5 text-center text-xs">
          <div className="p-3 rounded-xl bg-lo-surface-cream border border-lo-border-subtle">
            <span className="text-[10px] text-lo-text-subtle block mb-1">1. Total Likuiditas</span>
            <span className="font-semibold text-lo-text-ink tabular-nums text-xs sm:text-sm">
              {formatCurrencyRaw(derived.totalLiquidity)}
            </span>
          </div>
          <div className="p-3 rounded-xl bg-lo-warning-soft border border-lo-warning/20">
            <span className="text-[10px] text-lo-warning block mb-1">2. Komitmen Hari Ini</span>
            <span className="font-semibold text-lo-warning tabular-nums text-xs sm:text-sm">
              − {formatCurrencyRaw(derived.commitmentTotal)}
            </span>
          </div>
          <div className="p-3 rounded-xl bg-lo-accent-wash/60 border border-lo-secondary/20">
            <span className="text-[10px] text-lo-secondary font-medium block mb-1">3. Uang Bebas</span>
            <span className="font-bold text-lo-secondary tabular-nums text-xs sm:text-sm">
              {formatCurrencyRaw(derived.freeCash)}
            </span>
          </div>
        </div>

        <div className="p-4 rounded-xl bg-lo-surface-cream border border-lo-border-hairline space-y-3">
          <div className="flex items-center justify-between text-xs">
            <div className="flex items-center gap-2">
              <span className="w-1.5 h-1.5 rounded-full bg-lo-secondary" />
              <span className="text-lo-text-ink font-medium">Total Kas Likuid Terverifikasi</span>
            </div>
            <span className="font-semibold text-lo-text-ink tabular-nums text-sm">
              {formatCurrencyRaw(derived.totalLiquidity)}
            </span>
          </div>
          <div className="pl-3.5 space-y-2 border-l-2 border-lo-border-hairline text-xs">
            <div className="flex items-center justify-between text-lo-warning">
              <span>[-] Tagihan Jatuh Tempo (WiFi &amp; Listrik)</span>
              <span className="font-semibold tabular-nums">
                − {formatCurrencyRaw(derived.billsDueTotal)}
              </span>
            </div>
            <div className="flex items-center justify-between text-lo-text-subtle">
              <span>[-] Tabungan Wajib &amp; Pos Alokasi Rutin</span>
              <span className="font-semibold tabular-nums">
                − {formatCurrencyRaw(derived.savingsCommitment)}
              </span>
            </div>
          </div>
          <div className="pt-3 flex items-center justify-between border-t border-lo-border-hairline">
            <div className="space-y-0.5">
              <span className="font-bold text-xs text-lo-primary flex items-center gap-1.5">
                <Icon name="verified" className="text-[16px] text-lo-secondary" />
                Uang Bebas Bersih Siap Pakai
              </span>
              <span className="text-[11px] text-lo-text-subtle block">
                Aman tanpa khawatir mengganggu pos lain
              </span>
            </div>
            <span className="font-headline text-lg font-semibold text-lo-secondary tabular-nums">
              {formatCurrencyRaw(derived.freeCash)}
            </span>
          </div>
        </div>

        <div className="pt-1 flex items-center justify-between gap-4">
          <p className="text-[11px] text-lo-text-subtle leading-relaxed flex items-center gap-1.5">
            <Icon name="auto_mode" className="text-sm text-lo-secondary shrink-0" />
            <span>Nilai ini terupdate seketika tiap tagihan lunas.</span>
          </p>
          <button type="button" onClick={onClose} className={btnPrimary}>
            Saya Mengerti
          </button>
        </div>
      </div>
    </Modal>
  );
}

export default function UangBebasSection({
  derived,
  billsDue,
  onPayBill,
  onPayAll,
  onPostponeBill,
}: {
  derived: FinanceDerived;
  billsDue: BillDue[];
  onPayBill: (id: string) => void;
  onPayAll: () => void;
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
