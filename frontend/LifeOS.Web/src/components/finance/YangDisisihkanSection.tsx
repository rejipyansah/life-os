import { useMemo, useState } from 'react';

import {
  formatCurrencyRaw,
  formatNumberString,
  parseFormattedNumber,
  posCategoryLabel,
  type Account,
  type CreatePosInput,
  type PosCategory,
  type PosItem,
} from '../../finance';
import {
  btnPrimary,
  btnSecondary,
  cardBase,
  ConfirmDialog,
  EmptyState,
  Icon,
  inputBase,
  inputBaseSm,
  Modal,
  Pagination,
  ProgressBar,
  selectBase,
} from './shared';

const POS_PER_PAGE = 6;

type ModalMode =
  | null
  | { kind: 'create' }
  | { kind: 'savings'; pos: PosItem }
  | { kind: 'incremental'; pos: PosItem }
  | { kind: 'batch'; pos: PosItem }
  | { kind: 'single'; pos: PosItem };

interface YangDisisihkanSectionProps {
  posItems: PosItem[];
  accounts: Account[];
  filter: PosCategory | 'all';
  page: number;
  onFilterChange: (f: PosCategory | 'all') => void;
  onPageChange: (p: number) => void;
  onTopUp: (id: string, amount: number) => void;
  onWithdraw: (id: string, amount: number) => void;
  onUseIncremental: (id: string, amount: number, note?: string) => void;
  onExecuteBatch: (id: string, actualCost: number, note?: string) => void;
  onExecuteSingle: (id: string, actualCost: number, note?: string) => void;
  onCreate: (input: CreatePosInput) => void;
  onDelete: (id: string) => void;
}

const FILTERS: Array<{ key: PosCategory | 'all'; label: string }> = [
  { key: 'all', label: 'Semua Pos' },
  { key: 'saving', label: 'Tabungan & Simpanan' },
  { key: 'routine_incremental', label: 'Rutinitas Bertahap' },
  { key: 'routine_batch', label: 'Rutinitas Berkala' },
  { key: 'single_spend', label: 'Sekali Pakai' },
];

export default function YangDisisihkanSection({
  posItems,
  accounts,
  filter,
  page,
  onFilterChange,
  onPageChange,
  onTopUp,
  onWithdraw,
  onUseIncremental,
  onExecuteBatch,
  onExecuteSingle,
  onCreate,
  onDelete,
}: YangDisisihkanSectionProps) {
  const [modal, setModal] = useState<ModalMode>(null);
  const [deleteTarget, setDeleteTarget] = useState<PosItem | null>(null);

  const activeItems = useMemo(
    () => posItems.filter((p) => !p.archived),
    [posItems]
  );

  const filtered = useMemo(
    () => (filter === 'all' ? activeItems : activeItems.filter((p) => p.category === filter)),
    [activeItems, filter]
  );

  const totalPages = Math.max(1, Math.ceil(filtered.length / POS_PER_PAGE));
  const safePage = Math.min(page, totalPages);
  const startIndex = (safePage - 1) * POS_PER_PAGE;
  const pageItems = filtered.slice(startIndex, startIndex + POS_PER_PAGE);

  const filterCount = (key: PosCategory | 'all') =>
    key === 'all' ? activeItems.length : activeItems.filter((p) => p.category === key).length;

  return (
    <div className={`${cardBase} p-6 h-full flex flex-col`}>
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-3 pb-4 border-b border-lo-border-hairline/60">
        <div className="min-w-0">
          <div className="flex items-center gap-2.5 flex-wrap">
            <h2 className="font-headline text-2xl font-medium text-lo-text-ink tracking-tight">
              Yang Disisihkan
            </h2>
            <span className="text-[12px] bg-lo-accent-wash/80 text-lo-secondary px-3 py-1 rounded-full font-medium border border-lo-secondary/20">
              {activeItems.length} Pos Terisolasi
            </span>
          </div>
          <p className="text-xs text-lo-text-subtle mt-1">
            Alokasi tenang untuk tabungan, plafon belanja, dan cadangan.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setModal({ kind: 'create' })}
          className="inline-flex items-center gap-2 bg-lo-primary text-lo-surface-cream hover:bg-lo-primary-hover px-4 py-2.5 rounded-full text-xs font-medium transition-all cursor-pointer shadow-sm active:scale-95 shrink-0"
        >
          <Icon name="add" className="text-base" />
          <span>Buat Pos Baru</span>
        </button>
      </div>

      {/* Filter pills */}
      <div className="flex items-center gap-2 overflow-x-auto py-4 -mx-1 px-1">
        {FILTERS.map((f) => {
          const isActive = filter === f.key;
          return (
            <button
              key={f.key}
              type="button"
              onClick={() => onFilterChange(f.key)}
              className={`px-4 py-2 rounded-full text-xs transition-all shrink-0 cursor-pointer ${
                isActive
                  ? 'bg-lo-secondary text-lo-surface-cream font-medium shadow-xs'
                  : 'bg-lo-surface-recessed text-lo-text-subtle hover:text-lo-text-ink hover:bg-lo-container-low'
              }`}
            >
              {f.label} ({filterCount(f.key)})
            </button>
          );
        })}
      </div>

      {/* Items */}
      <div className="flex flex-col gap-3 flex-1">
        {pageItems.length === 0 ? (
          <EmptyState
            icon="savings"
            title="Tidak ada pos pada filter ini"
            hint="Buat pos baru atau pilih filter lain."
          />
        ) : (
          pageItems.map((pos) => (
            <PosCard
              key={pos.id}
              pos={pos}
              onOpen={() => openPosModal(pos, setModal)}
              onDelete={() => setDeleteTarget(pos)}
            />
          ))
        )}
      </div>

      <Pagination
        page={safePage}
        totalPages={totalPages}
        totalItems={filtered.length}
        onChange={onPageChange}
        labels={{ prev: 'Sebelumnya', next: 'Berikutnya' }}
        infoLabel={(from, to, total) =>
          total === 0
            ? 'Tidak ada pos yang cocok.'
            : `Menampilkan ${from}–${to} dari ${total} pos alokasi`
        }
      />

      {/* Create modal */}
      <CreatePosModal
        open={modal?.kind === 'create'}
        onClose={() => setModal(null)}
        accounts={accounts}
        onCreate={(input) => {
          onCreate(input);
          setModal(null);
        }}
      />

      {/* Savings modal */}
      {modal?.kind === 'savings' ? (
        <SavingsModal
          pos={modal.pos}
          onClose={() => setModal(null)}
          onTopUp={(amt) => {
            onTopUp(modal.pos.id, amt);
            setModal(null);
          }}
          onWithdraw={(amt) => {
            onWithdraw(modal.pos.id, amt);
            setModal(null);
          }}
          onDelete={() => {
            setDeleteTarget(modal.pos);
            setModal(null);
          }}
        />
      ) : null}

      {/* Incremental modal */}
      {modal?.kind === 'incremental' ? (
        <IncrementalModal
          pos={modal.pos}
          onClose={() => setModal(null)}
          onSubmit={(amt, note) => {
            onUseIncremental(modal.pos.id, amt, note);
            setModal(null);
          }}
        />
      ) : null}

      {/* Batch modal */}
      {modal?.kind === 'batch' ? (
        <BatchModal
          pos={modal.pos}
          onClose={() => setModal(null)}
          onSubmit={(cost, note) => {
            onExecuteBatch(modal.pos.id, cost, note);
            setModal(null);
          }}
        />
      ) : null}

      {/* Single spend modal */}
      {modal?.kind === 'single' ? (
        <SingleSpendModal
          pos={modal.pos}
          onClose={() => setModal(null)}
          onSubmit={(cost, note) => {
            onExecuteSingle(modal.pos.id, cost, note);
            setModal(null);
          }}
        />
      ) : null}

      {/* Delete confirm */}
      <ConfirmDialog
        open={deleteTarget !== null}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => {
          if (deleteTarget) onDelete(deleteTarget.id);
        }}
        title={`Hapus Pos ${deleteTarget?.name ?? ''}?`}
        description="Pos ini akan dihapus dari daftar aktif. Dana yang tersisa dikembalikan ke kas / Uang Bebas."
        icon="delete"
        actionLabel="Hapus Pos"
        danger
      />
    </div>
  );
}

function openPosModal(pos: PosItem, setModal: (m: ModalMode) => void) {
  if (pos.category === 'saving') setModal({ kind: 'savings', pos });
  else if (pos.category === 'routine_incremental') setModal({ kind: 'incremental', pos });
  else if (pos.category === 'routine_batch') setModal({ kind: 'batch', pos });
  else setModal({ kind: 'single', pos });
}

function PosCard({
  pos,
  onOpen,
  onDelete,
}: {
  pos: PosItem;
  onOpen: () => void;
  onDelete: () => void;
}) {
  const categoryLabel = posCategoryLabel(pos.category);
  const hasTarget = typeof pos.targetAmount === 'number' && pos.targetAmount > 0;
  const targetPct = hasTarget
    ? Math.min(100, Math.round((pos.amount / (pos.targetAmount as number)) * 100))
    : 0;
  const usedPct =
    pos.plafon && pos.plafon > 0
      ? Math.min(100, Math.round(((pos.usedAmount || 0) / pos.plafon) * 100))
      : 0;

  const actionLabel =
    pos.category === 'routine_incremental'
      ? 'Catat Pakai'
      : pos.category === 'routine_batch'
        ? 'Eksekusi Servis'
        : pos.category === 'single_spend'
          ? 'Realisasikan Belanja'
          : hasTarget
            ? 'Tarik / Tambah'
            : 'Kelola Simpanan';

  const rightStatus =
    pos.category === 'routine_incremental'
      ? `Terpakai ${formatCurrencyRaw(pos.usedAmount || 0)}`
      : pos.category === 'routine_batch'
        ? 'Target terpenuh'
        : pos.category === 'single_spend'
          ? 'Dana terkumpul penuh'
          : pos.status || (hasTarget ? `${targetPct}% tercapai` : 'Fleksibel');

  return (
    <div className="group bg-lo-surface-cream/70 hover:bg-lo-surface-recessed/50 transition-all duration-200 rounded-2xl p-5 border border-lo-border-hairline/60 hover:border-lo-secondary/30">
      <div className="flex items-start justify-between gap-4">
        <button
          type="button"
          onClick={onOpen}
          className="flex items-start gap-4 min-w-0 text-left flex-1 cursor-pointer"
        >
          <div className="w-11 h-11 rounded-xl bg-lo-accent-wash flex items-center justify-center text-lo-secondary shrink-0 border border-lo-secondary/15">
            <Icon name={pos.icon} className="text-2xl" />
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-2.5 flex-wrap">
              <h3 className="font-headline-sm text-base font-semibold text-lo-text-ink truncate">
                {pos.name}
              </h3>
              <span className="inline-flex items-center gap-1 text-[11px] bg-lo-accent-wash text-lo-secondary px-2.5 py-0.5 rounded-full font-medium">
                {categoryLabel}
              </span>
            </div>
            <p className="text-xs text-lo-text-subtle mt-1 truncate">{pos.description}</p>
          </div>
        </button>
        <div className="text-right shrink-0">
          <div className="font-headline text-[22px] font-semibold tabular-nums tracking-tight text-lo-text-ink leading-tight">
            {pos.category === 'routine_incremental'
              ? `Sisa ${formatCurrencyRaw(pos.amount)}`
              : formatCurrencyRaw(pos.amount)}
          </div>
          <div className="text-[11px] text-lo-text-subtle mt-0.5">
            {pos.category === 'routine_incremental'
              ? `${100 - usedPct}% aman`
              : hasTarget
                ? `Target ${formatCurrencyRaw(pos.targetAmount as number)} (${targetPct}%)`
                : pos.category === 'routine_batch'
                  ? `${formatCurrencyRaw(pos.plafon || pos.amount)} (100% Siap)`
                  : pos.category === 'single_spend'
                    ? `${pos.status || 'Siap'}`
                    : ''}
          </div>
        </div>
      </div>

      {/* Progress */}
      {pos.category === 'saving' && hasTarget ? (
        <div className="mt-4 pt-3 border-t border-lo-border-hairline/40">
          <ProgressBar percent={targetPct} />
        </div>
      ) : null}
      {pos.category === 'routine_incremental' && pos.plafon ? (
        <div className="mt-4 pt-3 border-t border-lo-border-hairline/40">
          <ProgressBar percent={usedPct} />
        </div>
      ) : null}
      {pos.category === 'routine_batch' ? (
        <div className="mt-4 pt-3 border-t border-lo-border-hairline/40">
          <ProgressBar percent={pos.amount > 0 ? 100 : 0} />
        </div>
      ) : null}
      {pos.category === 'single_spend' && hasTarget ? (
        <div className="mt-4 pt-3 border-t border-lo-border-hairline/40">
          <ProgressBar percent={targetPct} />
        </div>
      ) : null}

      {/* Bottom action row */}
      <div
        className="mt-3 pt-3 border-t border-lo-border-hairline/50 flex items-center justify-between gap-2"
        onClick={(e) => e.stopPropagation()}
      >
        <button
          type="button"
          onClick={onOpen}
          className={
            pos.category === 'routine_batch' || pos.category === 'single_spend'
              ? `${btnPrimary} px-3.5 py-1.5 text-xs`
              : `${btnSecondary} px-3.5 py-1.5 text-xs`
          }
        >
          <Icon
            name={
              pos.category === 'routine_incremental'
                ? 'edit_note'
                : pos.category === 'routine_batch'
                  ? 'build'
                  : pos.category === 'single_spend'
                    ? 'shopping_cart_checkout'
                    : 'savings'
            }
            className="text-[16px]"
          />
          <span>{actionLabel}</span>
        </button>
        <div className="flex items-center gap-2">
          <span className="text-[11px] text-lo-text-subtle">{rightStatus}</span>
          <button
            type="button"
            onClick={onDelete}
            title="Hapus pos"
            className="w-8 h-8 rounded-lg flex items-center justify-center text-lo-text-subtle hover:text-lo-error hover:bg-red-50 transition-all cursor-pointer opacity-0 group-hover:opacity-100"
          >
            <Icon name="delete" className="text-[16px]" />
          </button>
        </div>
      </div>
    </div>
  );
}

/* ── Create pos modal ── */

function CreatePosModal({
  open,
  onClose,
  accounts,
  onCreate,
}: {
  open: boolean;
  onClose: () => void;
  accounts: Account[];
  onCreate: (input: CreatePosInput) => void;
}) {
  const [name, setName] = useState('');
  const [category, setCategory] = useState<PosCategory>('saving');
  const [accountLabel, setAccountLabel] = useState(accounts[0]?.name || 'SeaBank');
  const [target, setTarget] = useState('');
  const [plafon, setPlafon] = useState('');
  const [cycle, setCycle] = useState('Bulanan');
  const [description, setDescription] = useState('');

  const canSubmit = name.trim().length >= 2;

  const reset = () => {
    setName('');
    setCategory('saving');
    setTarget('');
    setPlafon('');
    setCycle('Bulanan');
    setDescription('');
  };

  return (
    <Modal
      open={open}
      onClose={() => {
        reset();
        onClose();
      }}
      title="Buat Pos Baru"
      icon="add_circle"
      maxWidth="max-w-2xl"
      footer={
        <>
          <button
            type="button"
            onClick={() => {
              reset();
              onClose();
            }}
            className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink transition-colors cursor-pointer"
          >
            Batal
          </button>
          <button
            type="button"
            disabled={!canSubmit}
            onClick={() => {
              onCreate({
                name: name.trim(),
                description:
                  description.trim() ||
                  (category === 'saving' ? 'Simpanan fleksibel' : 'Pos dana baru'),
                category,
                accountLabel,
                targetAmount:
                  category === 'saving' && target ? parseFormattedNumber(target) : undefined,
                plafon:
                  category === 'routine_incremental' || category === 'routine_batch'
                    ? parseFormattedNumber(plafon) || undefined
                    : undefined,
                cycle: category === 'routine_batch' ? cycle : undefined,
              });
              reset();
            }}
            className={`${btnPrimary} disabled:opacity-40 disabled:hover:bg-lo-primary`}
          >
            <Icon name="check" className="text-base" />
            <span>Simpan Pos Baru</span>
          </button>
        </>
      }
    >
      <div className="flex flex-col gap-5">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-type">
              Karakter &amp; Tipe Pos Dana <span className="text-lo-secondary">*</span>
            </label>
            <select
              id="new-pos-type"
              className={selectBase}
              value={category}
              onChange={(e) => setCategory(e.target.value as PosCategory)}
            >
              <option value="saving">Tabungan &amp; Simpanan Organik</option>
              <option value="routine_incremental">Rutinitas Bertahap</option>
              <option value="routine_batch">Rutinitas Berkala 1x Pakai</option>
              <option value="single_spend">Sekali Pakai Non-Rutin</option>
            </select>
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-account">
              Rekening Sumber <span className="text-lo-secondary">*</span>
            </label>
            <select
              id="new-pos-account"
              className={selectBase}
              value={accountLabel}
              onChange={(e) => setAccountLabel(e.target.value)}
            >
              {accounts.map((a) => (
                <option key={a.id} value={a.name}>
                  {a.name}
                </option>
              ))}
              <option value="BCA Syariah">BCA Syariah</option>
            </select>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-name">
            Nama Pos <span className="text-lo-secondary">*</span>
          </label>
          <input
            id="new-pos-name"
            type="text"
            className={inputBase}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Misal: Dana Makan Harian, Servis Mobil, Beli Sofa Baru"
          />
        </div>

        {category === 'saving' ? (
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-target">
              Target Tabungan (Rp)
            </label>
            <input
              id="new-pos-target"
              type="text"
              className={inputBase}
              value={target}
              onChange={(e) => setTarget(formatNumberString(e.target.value))}
              placeholder="Kosongkan jika simpanan bebas fleksibel"
            />
            <span className="text-[11px] text-lo-text-subtle">
              Saldo awal selalu mulai dari Rp 0 dan diisi bertahap lewat Top-Up.
            </span>
          </div>
        ) : null}

        {category === 'routine_incremental' ? (
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-plafon">
              Plafon Siklus (Rp) <span className="text-lo-secondary">*</span>
            </label>
            <input
              id="new-pos-plafon"
              type="text"
              className={inputBase}
              value={plafon}
              onChange={(e) => setPlafon(formatNumberString(e.target.value))}
              placeholder="Contoh: 300000"
            />
          </div>
        ) : null}

        {category === 'routine_batch' ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-cycle">
                Periode Eksekusi
              </label>
              <select
                id="new-pos-cycle"
                className={selectBase}
                value={cycle}
                onChange={(e) => setCycle(e.target.value)}
              >
                <option value="Bulanan">Bulanan</option>
                <option value="Per 3 Bulan">Per 3 Bulan</option>
                <option value="Per 6 Bulan">Per 6 Bulan</option>
                <option value="Tahunan">Tahunan</option>
              </select>
            </div>
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-batch-cost">
                Estimasi Biaya 1x Eksekusi (Rp) <span className="text-lo-secondary">*</span>
              </label>
              <input
                id="new-pos-batch-cost"
                type="text"
                className={inputBase}
                value={plafon}
                onChange={(e) => setPlafon(formatNumberString(e.target.value))}
                placeholder="Contoh: 500000"
              />
            </div>
          </div>
        ) : null}

        {category === 'single_spend' ? (
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-single-budget">
              Target Anggaran Belanja (Rp) <span className="text-lo-secondary">*</span>
            </label>
            <input
              id="new-pos-single-budget"
              type="text"
              className={inputBase}
              value={plafon}
              onChange={(e) => setPlafon(formatNumberString(e.target.value))}
              placeholder="Contoh: 400000"
            />
            <span className="text-[11px] text-lo-text-subtle">
              Setelah dibelanjakan tuntas, pos akan dipindahkan dari daftar aktif.
            </span>
          </div>
        ) : null}

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="new-pos-note">
            Catatan Pos
          </label>
          <textarea
            id="new-pos-note"
            className={`${inputBase} resize-none`}
            rows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Tuliskan pedoman atau catatan khusus..."
          />
        </div>

        {!canSubmit ? (
          <p className="text-[11px] text-lo-text-subtle">Isi nama pos untuk melanjutkan.</p>
        ) : null}
      </div>
    </Modal>
  );
}

/* ── Savings modal ── */

function SavingsModal({
  pos,
  onClose,
  onTopUp,
  onWithdraw,
  onDelete,
}: {
  pos: PosItem;
  onClose: () => void;
  onTopUp: (amount: number) => void;
  onWithdraw: (amount: number) => void;
  onDelete: () => void;
}) {
  const [mode, setMode] = useState<'topup' | 'withdraw'>('topup');
  const [amountStr, setAmountStr] = useState('');
  const amount = parseFormattedNumber(amountStr);
  const canSubmit = amount > 0 && (mode === 'topup' || amount <= pos.amount);

  const setQuick = (v: number) => {
    if (mode === 'topup') {
      setAmountStr(String(parseFormattedNumber(amountStr) + v));
    } else {
      setAmountStr(String(Math.min(v, pos.amount)));
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Kelola Simpanan: ${pos.name}`}
      icon="savings"
      footer={
        <>
          <button
            type="button"
            onClick={onDelete}
            className="text-xs text-lo-warning hover:underline inline-flex items-center gap-1 cursor-pointer mr-auto"
          >
            <Icon name="delete" className="text-base" />
            Hapus Pos
          </button>
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink transition-colors cursor-pointer"
          >
            Tutup
          </button>
          <button
            type="button"
            disabled={!canSubmit}
            onClick={() => {
              if (mode === 'topup') onTopUp(amount);
              else onWithdraw(amount);
            }}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            {mode === 'topup' ? 'Konfirmasi Tambah Simpanan' : 'Konfirmasi Tarik Simpanan'}
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-xs text-lo-text-subtle">{pos.description}</p>
        <div className="bg-lo-surface-recessed p-4 rounded-xl flex items-center justify-between">
          <div>
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Saldo Tabungan Saat Ini
            </span>
            <span className="text-2xl font-semibold tabular-nums text-lo-text-ink">
              {formatCurrencyRaw(pos.amount)}
            </span>
            <span className="text-[12px] text-lo-text-subtle block mt-0.5">
              {pos.accountLabel} · Terisolasi Aman
            </span>
          </div>
          <div className="text-right">
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Target Capaian
            </span>
            <span className="text-base font-semibold text-lo-text-ink">
              {pos.targetAmount ? formatCurrencyRaw(pos.targetAmount) : 'Fleksibel'}
            </span>
          </div>
        </div>

        <div className="p-4 rounded-xl border border-lo-border-hairline bg-lo-container-low">
          <div className="flex items-center p-1 rounded-lg bg-lo-surface-recessed border border-lo-border-hairline mb-3">
            <button
              type="button"
              onClick={() => setMode('topup')}
              className={`flex-1 py-1.5 rounded-md text-xs font-semibold transition-all cursor-pointer inline-flex items-center justify-center gap-1.5 ${
                mode === 'topup'
                  ? 'bg-lo-secondary text-lo-surface-cream shadow-xs'
                  : 'text-lo-text-subtle hover:text-lo-text-ink'
              }`}
            >
              <Icon name="add_circle" className="text-base" />
              Tambah Simpanan (Top-Up)
            </button>
            <button
              type="button"
              onClick={() => setMode('withdraw')}
              className={`flex-1 py-1.5 rounded-md text-xs font-medium transition-all cursor-pointer inline-flex items-center justify-center gap-1.5 ${
                mode === 'withdraw'
                  ? 'bg-lo-secondary text-lo-surface-cream shadow-xs'
                  : 'text-lo-text-subtle hover:text-lo-text-ink'
              }`}
            >
              <Icon name="remove_circle" className="text-base" />
              Tarik Simpanan
            </button>
          </div>
          <p className="text-[12px] text-lo-text-subtle mb-3">
            {mode === 'topup'
              ? 'Setor dana dari Uang Bebas untuk menambah akumulasi tabungan ini.'
              : 'Tarik sebagian simpanan. Dana yang ditarik akan langsung dikembalikan ke Kas / Uang Bebas Anda.'}
          </p>
          <div className="grid grid-cols-4 gap-2 mb-3">
            {[50_000, 100_000, 250_000, 500_000].map((v) => (
              <button
                key={v}
                type="button"
                onClick={() => setQuick(v)}
                className="py-2 rounded-lg text-[11px] bg-lo-surface-cream hover:bg-lo-accent-wash text-lo-text-ink border border-lo-border-hairline font-medium text-center transition-all cursor-pointer"
              >
                {v / 1000}rb
              </button>
            ))}
          </div>
          <div className="relative">
            <span className="absolute left-3.5 top-2.5 text-[11px] text-lo-text-subtle">Rp</span>
            <input
              type="text"
              className={`${inputBase} pl-9 font-semibold tabular-nums`}
              value={amountStr}
              onChange={(e) => setAmountStr(formatNumberString(e.target.value))}
              placeholder="Ketik nominal transfer..."
            />
          </div>
          <div className="mt-3 p-3 rounded-xl bg-lo-surface-recessed border border-lo-border-hairline text-[12px] text-lo-text-subtle">
            {mode === 'topup'
              ? 'Menyisihkan dana ke pos simpanan akan mengurangi Uang Bebas aktif Anda.'
              : `Maksimal tarik: ${formatCurrencyRaw(pos.amount)}.`}
          </div>
        </div>
      </div>
    </Modal>
  );
}

/* ── Incremental modal ── */

function IncrementalModal({
  pos,
  onClose,
  onSubmit,
}: {
  pos: PosItem;
  onClose: () => void;
  onSubmit: (amount: number, note?: string) => void;
}) {
  const [amountStr, setAmountStr] = useState('');
  const [note, setNote] = useState('');
  const amount = parseFormattedNumber(amountStr);
  const canSubmit = amount > 0 && amount <= pos.amount;

  return (
    <Modal
      open
      onClose={onClose}
      title="Catat Pengeluaran Bertahap"
      icon="receipt_long"
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
            disabled={!canSubmit}
            onClick={() => onSubmit(amount, note || undefined)}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="check" className="text-base" />
            <span>Simpan Pemakaian</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="bg-lo-surface-recessed p-4 rounded-xl flex items-center justify-between">
          <div>
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Plafon Siklus Aktif
            </span>
            <span className="text-base font-semibold text-lo-text-ink">{pos.name}</span>
            <span className="text-[12px] text-lo-text-subtle block mt-0.5">
              {pos.accountLabel} · Plafon {formatCurrencyRaw(pos.plafon || 0)}
            </span>
          </div>
          <div className="text-right">
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Sisa Plafon Siap Pakai
            </span>
            <span className="text-2xl font-semibold tabular-nums text-lo-text-ink">
              {formatCurrencyRaw(pos.amount)}
            </span>
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="inc-amount">
            Nominal Pemakaian Kali Ini <span className="text-lo-secondary">*</span>
          </label>
          <div className="grid grid-cols-4 gap-2">
            {[15_000, 25_000, 50_000, 100_000].map((v) => (
              <button
                key={v}
                type="button"
                onClick={() => setAmountStr(String(Math.min(v, pos.amount)))}
                className="py-2 rounded-lg text-[11px] bg-lo-surface-cream hover:bg-lo-accent-wash text-lo-text-ink border border-lo-border-hairline font-medium text-center transition-all cursor-pointer"
              >
                {v / 1000}rb
              </button>
            ))}
          </div>
          <div className="relative">
            <span className="absolute left-3.5 top-2.5 text-xs text-lo-text-subtle">Rp</span>
            <input
              id="inc-amount"
              type="text"
              className={`${inputBase} pl-11 font-semibold tabular-nums text-base`}
              value={amountStr}
              onChange={(e) => setAmountStr(formatNumberString(e.target.value))}
              placeholder="0"
            />
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="inc-note">
            Keperluan / Catatan Transaksi
          </label>
          <input
            id="inc-note"
            type="text"
            className={inputBaseSm}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Misal: makan siang warteg"
          />
        </div>

        {amount > 0 ? (
          <div className="p-3 rounded-xl bg-lo-accent-wash/60 border border-lo-secondary/20 text-xs text-lo-text-ink">
            Pemakaian{' '}
            <span className="font-semibold font-headline">{formatCurrencyRaw(amount)}</span> akan
            mengurangi sisa plafon menjadi{' '}
            <span className="font-semibold font-headline">
              {formatCurrencyRaw(Math.max(0, pos.amount - amount))}
            </span>
            .
          </div>
        ) : null}
      </div>
    </Modal>
  );
}

/* ── Batch modal ── */

function BatchModal({
  pos,
  onClose,
  onSubmit,
}: {
  pos: PosItem;
  onClose: () => void;
  onSubmit: (cost: number, note?: string) => void;
}) {
  const defaultCost = pos.plafon || pos.amount;
  const [amountStr, setAmountStr] = useState(String(defaultCost));
  const [note, setNote] = useState('');
  const amount = parseFormattedNumber(amountStr) || defaultCost;

  return (
    <Modal
      open
      onClose={onClose}
      title="Eksekusi Rutinitas Berkala (1x Pakai)"
      icon="event_repeat"
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
            onClick={() => onSubmit(amount, note || undefined)}
            className={btnPrimary}
          >
            <Icon name="check" className="text-base" />
            <span>Konfirmasi Eksekusi Tuntas</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="bg-lo-surface-recessed p-4 rounded-xl flex items-center justify-between">
          <div>
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Rutinitas Periode Ini
            </span>
            <span className="text-base font-semibold text-lo-text-ink">{pos.name}</span>
            <span className="text-[12px] text-lo-text-subtle block mt-0.5">
              {pos.accountLabel} · {pos.cycle || 'Bulanan'}
            </span>
          </div>
          <div className="text-right">
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Dana Disiapkan
            </span>
            <span className="text-2xl font-semibold tabular-nums text-lo-secondary">
              {formatCurrencyRaw(pos.amount)}
            </span>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <div className="flex items-center justify-between">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="batch-cost">
              Biaya Aktual Servis / Eksekusi (Rp)
            </label>
            <button
              type="button"
              onClick={() => setAmountStr(String(defaultCost))}
              className="text-[11px] text-lo-secondary hover:underline cursor-pointer font-medium"
            >
              Pas Plafon ({formatCurrencyRaw(defaultCost)})
            </button>
          </div>
          <div className="relative">
            <span className="absolute left-3.5 top-2.5 text-xs text-lo-text-subtle">Rp</span>
            <input
              id="batch-cost"
              type="text"
              className={`${inputBase} pl-11 font-semibold tabular-nums text-base`}
              value={amountStr}
              onChange={(e) => setAmountStr(formatNumberString(e.target.value))}
              placeholder={String(defaultCost)}
            />
          </div>
        </div>

        <div className="p-3.5 rounded-xl border border-lo-secondary/40 bg-lo-accent-wash/60">
          <div className="flex items-center justify-between">
            <span className="text-[11px] text-lo-text-subtle">Kondisi Realisasi:</span>
            <span className="text-xs font-semibold text-lo-secondary">
              {amount === defaultCost ? 'Sesuai Estimasi Pas Plafon' : 'Berbeda dari Estimasi'}
            </span>
          </div>
          <p className="text-[12px] text-lo-text-subtle mt-1">
            Dana {formatCurrencyRaw(amount)} digunakan 1x tuntas. Status periode ini akan
            ditandai Selesai dan siap untuk siklus berikutnya.
          </p>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="batch-note">
            Catatan Bengkel / Vendor (Opsional)
          </label>
          <input
            id="batch-note"
            type="text"
            className={inputBaseSm}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Contoh: Ganti oli Shell Advance + kampas rem depan"
          />
        </div>
      </div>
    </Modal>
  );
}

/* ── Single spend modal ── */

function SingleSpendModal({
  pos,
  onClose,
  onSubmit,
}: {
  pos: PosItem;
  onClose: () => void;
  onSubmit: (cost: number, note?: string) => void;
}) {
  const defaultCost = pos.targetAmount || pos.amount;
  const [amountStr, setAmountStr] = useState(String(defaultCost));
  const [note, setNote] = useState('');
  const amount = parseFormattedNumber(amountStr) || defaultCost;

  return (
    <Modal
      open
      onClose={onClose}
      title="Realisasikan Belanja Non-Rutin"
      icon="shopping_cart_checkout"
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
            onClick={() => onSubmit(amount, note || undefined)}
            className={btnPrimary}
          >
            <Icon name="archive" className="text-base" />
            <span>Belanjakan &amp; Arsipkan Pos</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="bg-lo-surface-recessed p-4 rounded-xl flex items-center justify-between">
          <div>
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Pos Belanja Sekali Pakai
            </span>
            <span className="text-base font-semibold text-lo-text-ink">{pos.name}</span>
            <span className="text-[12px] text-lo-text-subtle block mt-0.5">
              {pos.accountLabel} · Target Tercapai
            </span>
          </div>
          <div className="text-right">
            <span className="text-[11px] text-lo-text-subtle block uppercase tracking-wide">
              Alokasi Tersedia
            </span>
            <span className="text-2xl font-semibold tabular-nums text-lo-secondary">
              {formatCurrencyRaw(pos.amount)}
            </span>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <div className="flex items-center justify-between">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="single-cost">
              Harga Pembelian Aktual (Rp)
            </label>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setAmountStr(String(Math.round(defaultCost * 0.925)))}
                className="text-[11px] text-lo-secondary hover:underline cursor-pointer font-medium"
              >
                Hemat ({Math.round((defaultCost * 0.925) / 1000)}rb)
              </button>
              <span className="text-lo-border-hairline">·</span>
              <button
                type="button"
                onClick={() => setAmountStr(String(defaultCost))}
                className="text-[11px] text-lo-secondary hover:underline cursor-pointer font-medium"
              >
                Pas ({Math.round(defaultCost / 1000)}rb)
              </button>
            </div>
          </div>
          <div className="relative">
            <span className="absolute left-3.5 top-2.5 text-xs text-lo-text-subtle">Rp</span>
            <input
              id="single-cost"
              type="text"
              className={`${inputBase} pl-11 font-semibold tabular-nums text-base`}
              value={amountStr}
              onChange={(e) => setAmountStr(formatNumberString(e.target.value))}
              placeholder={String(defaultCost)}
            />
          </div>
        </div>

        <div className="p-3.5 rounded-xl border border-lo-secondary/40 bg-lo-accent-wash/60">
          <div className="flex items-center justify-between">
            <span className="text-[11px] text-lo-text-subtle">Hasil Belanja:</span>
            <span className="text-xs font-semibold text-lo-secondary">
              {amount <= defaultCost ? 'Pas / Hemat dengan Alokasi' : 'Melebihi Alokasi'}
            </span>
          </div>
          <p className="text-[12px] text-lo-text-subtle mt-1">
            Barang berhasil dibeli. Pos akan ditandai Terpenuhi &amp; dipindahkan ke Arsip
            dengan rapi.
          </p>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="single-note">
            Catatan / Toko (Opsional)
          </label>
          <input
            id="single-note"
            type="text"
            className={inputBaseSm}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Misal: Optik Melawai Grand Indonesia"
          />
        </div>
      </div>
    </Modal>
  );
}
