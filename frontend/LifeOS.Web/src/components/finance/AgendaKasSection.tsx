import { useMemo, useState } from 'react';

import {
  formatCurrencyRaw,
  todayIso,
  TRANSACTION_CATEGORIES,
  type Account,
  type AgendaItem,
  type ArchivedAgenda,
  type CreateAgendaInput,
} from '../../finance';
import {
  btnPrimary,
  btnDanger,
  btnGhost,
  cardBase,
  ConfirmDialog,
  EmptyState,
  Icon,
  inputBase,
  inputBaseSm,
  Modal,
  Pagination,
  pillPassive,
  pillWarning,
  selectBase,
} from './shared';

const AGENDA_PER_PAGE = 5;
const EXPENSE_CATEGORIES = TRANSACTION_CATEGORIES.filter((category) => category !== 'Pendapatan');

interface AgendaKasSectionProps {
  accounts: Account[];
  agendas: AgendaItem[];
  archivedAgendas: ArchivedAgenda[];
  filter: 'all' | 'scheduled' | 'flexible';
  page: number;
  onFilterChange: (f: 'all' | 'scheduled' | 'flexible') => void;
  onPageChange: (p: number) => void;
  onSkip: (id: string) => void;
  onScheduleFlexible: (id: string, newDueDate: string) => Promise<boolean>;
  onStopCycle: (id: string) => void;
  onUpdate: (id: string, input: CreateAgendaInput) => Promise<boolean>;
  /** Selesaikan rencana memakai sumber dana aktual yang dipilih. */
  onFinish: (id: string, accountId: string) => Promise<boolean>;
  onCreate: (input: CreateAgendaInput) => Promise<boolean>;
}

type ConfirmKind = 'skip' | 'delete';

export default function AgendaKasSection({
  accounts,
  agendas,
  archivedAgendas,
  filter,
  page,
  onFilterChange,
  onPageChange,
  onSkip,
  onScheduleFlexible,
  onStopCycle,
  onUpdate,
  onFinish,
  onCreate,
}: AgendaKasSectionProps) {
  const [editorTarget, setEditorTarget] = useState<AgendaItem | 'new' | null>(null);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [flexiblePostponeTarget, setFlexiblePostponeTarget] = useState<AgendaItem | null>(null);
  const [confirm, setConfirm] = useState<{ kind: ConfirmKind; agenda: AgendaItem } | null>(
    null
  );
  // Realize modal — Sumber Dana dipilih SAAT rencana diselesaikan.
  const [finishTarget, setFinishTarget] = useState<AgendaItem | null>(null);

  const filtered = useMemo(() => {
    if (filter === 'scheduled') return agendas.filter((a) => a.type === 'scheduled');
    if (filter === 'flexible') return agendas.filter((a) => a.type === 'flexible');
    return agendas;
  }, [agendas, filter]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / AGENDA_PER_PAGE));
  const safePage = Math.min(page, totalPages);
  const startIndex = (safePage - 1) * AGENDA_PER_PAGE;
  const pageItems = filtered.slice(startIndex, startIndex + AGENDA_PER_PAGE);

  const countSched = agendas.filter((a) => a.type === 'scheduled').length;
  const countFlex = agendas.filter((a) => a.type === 'flexible').length;

  const confirmConfig = confirm ? buildConfirm(confirm.kind, confirm.agenda) : null;

  return (
    <div className={`${cardBase} p-6 sm:p-8`}>
      <header className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-5 border-b border-lo-border-hairline/80">
        <div className="flex items-center gap-3 flex-wrap">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-lo-secondary" />
            <h2 className="font-headline text-xl sm:text-2xl font-medium text-lo-text-ink tracking-tight">
              Rencana Pengeluaran/Pemasukan
            </h2>
          </div>
          <span className="px-2.5 py-0.5 rounded-full bg-lo-accent-wash text-lo-secondary text-xs font-medium">
            {agendas.length} Rencana
          </span>
          <button
            type="button"
            onClick={() => setEditorTarget('new')}
            className="ml-auto sm:ml-2 px-3.5 py-1.5 rounded-full bg-lo-primary hover:bg-lo-secondary text-white text-xs font-medium transition-all duration-150 flex items-center gap-1.5 shadow-sm active:scale-95 cursor-pointer"
          >
            <Icon name="add" className="text-[16px]" />
            <span>Buat Rencana</span>
          </button>
        </div>
        <button
          type="button"
          onClick={() => setArchiveOpen(true)}
          className="text-xs text-lo-text-subtle hover:text-lo-text-ink flex items-center gap-1.5 cursor-pointer transition-colors self-start sm:self-auto py-1"
        >
          <Icon name="history" className="text-[17px]" />
          <span>Lihat Arsip Rencana</span>
        </button>
      </header>

      <nav aria-label="Filter Agenda" className="flex items-center gap-1.5 p-1 bg-lo-surface-recessed rounded-full w-fit mt-4">
        {(
          [
            { key: 'all' as const, label: `Semua (${agendas.length})` },
            { key: 'scheduled' as const, label: `Terjadwal (${countSched})` },
            { key: 'flexible' as const, label: `Fleksibel (${countFlex})` },
          ]
        ).map((tab) => {
          const isActive = filter === tab.key;
          return (
            <button
              key={tab.key}
              type="button"
              onClick={() => onFilterChange(tab.key)}
              className={`px-3.5 py-1 rounded-full text-xs font-medium transition-all cursor-pointer ${
                isActive
                  ? 'bg-white text-lo-text-ink shadow-xs'
                  : 'text-lo-text-subtle hover:text-lo-text-ink'
              }`}
            >
              {tab.label}
            </button>
          );
        })}
      </nav>

      <section aria-label="Daftar Agenda Kas" className="space-y-3 mt-4">
        {pageItems.length === 0 ? (
          <EmptyState
            icon="event_busy"
            title="Belum ada Rencana Pengeluaran/Pemasukan"
            hint='Buat rencana untuk kebutuhan yang akan datang.'
          />
        ) : (
          pageItems.map((a) => (
            <AgendaCard
              key={a.id}
              agenda={a}
              onSkip={() => setConfirm({ kind: 'skip', agenda: a })}
              onScheduleFlexible={() => setFlexiblePostponeTarget(a)}
               onStopCycle={() => setConfirm({ kind: 'delete', agenda: a })}
              onEdit={() => setEditorTarget(a)}
              onFinish={() => setFinishTarget(a)}
            />
          ))
        )}
      </section>

      <Pagination
        page={safePage}
        totalPages={totalPages}
        totalItems={filtered.length}
        onChange={onPageChange}
        labels={{ prev: 'Sebelumnya', next: 'Berikutnya' }}
        infoLabel={(from, to, total) => (
          <>
            Menampilkan{' '}
            <span className="font-medium text-lo-text-ink">
              {total === 0 ? 0 : from}–{to}
            </span>{' '}
            dari <span className="font-medium text-lo-text-ink">{total}</span> agenda
          </>
        )}
      />

      <CreateAgendaModal
        key={editorTarget === 'new' ? 'new' : editorTarget?.id ?? 'closed'}
        open={editorTarget !== null}
        accounts={accounts}
        initialAgenda={editorTarget && editorTarget !== 'new' ? editorTarget : undefined}
        onClose={() => setEditorTarget(null)}
        onSave={async (input) => {
          const succeeded = editorTarget && editorTarget !== 'new'
            ? await onUpdate(editorTarget.id, input)
            : await onCreate(input);
          if (succeeded) setEditorTarget(null);
          return succeeded;
        }}
      />

      {flexiblePostponeTarget ? (
        <FlexiblePostponeModal
          key={flexiblePostponeTarget.id}
          agenda={flexiblePostponeTarget}
          onClose={() => setFlexiblePostponeTarget(null)}
          onPostpone={async (newDueDate) => {
            const succeeded = await onScheduleFlexible(flexiblePostponeTarget.id, newDueDate);
            if (succeeded) setFlexiblePostponeTarget(null);
            return succeeded;
          }}
        />
      ) : null}

      <ArchiveModal
        open={archiveOpen}
        onClose={() => setArchiveOpen(false)}
        items={archivedAgendas}
      />

      {/* Realize modal — rekening aktual dipilih di sini; default hanya preselect.
          key memaksa remount per rencana sehingga state selector selalu segar. */}
      {finishTarget ? (
        <FinishAgendaModal
          key={finishTarget.id}
          agenda={finishTarget}
          accounts={accounts}
          onClose={() => setFinishTarget(null)}
          onFinish={async (accountId) => {
            const succeeded = await onFinish(finishTarget.id, accountId);
            if (succeeded) setFinishTarget(null);
            return succeeded;
          }}
        />
      ) : null}

      {confirm && confirmConfig ? (
        <ConfirmDialog
          open
          onClose={() => setConfirm(null)}
          onConfirm={() => {
            const id = confirm.agenda.id;
            if (confirm.kind === 'skip') onSkip(id);
            else onStopCycle(id);
          }}
          title={confirmConfig.title}
          description={confirmConfig.desc}
          icon={confirmConfig.icon}
          actionLabel={confirmConfig.actionLabel}
          detailTitle={confirm.agenda.title}
          detailAmount={`${confirm.agenda.isIncome ? '+' : '-'} ${formatCurrencyRaw(confirm.agenda.amount)}`}
          detailAccount={confirm.agenda.displayDate}
          detailAccountLabel="Tanggal"
          danger={confirm.kind === 'delete'}
        />
      ) : null}
    </div>
  );
}

function buildConfirm(kind: ConfirmKind, a: AgendaItem) {
  switch (kind) {
    case 'skip':
      return {
        title: 'Lewati Agenda Kas?',
        desc: a.isIncome
          ? `Kejadian pemasukan "${a.title}" ini dilewati dan diarsipkan. Jika berulang, siklus berikutnya akan dijadwalkan.`
          : `Siklus "${a.title}" ini dilewati dan diarsipkan. Jika berulang, siklus berikutnya akan dijadwalkan; komitmen Dana Bebas untuk siklus ini dilepas.`,
        icon: 'redo',
        actionLabel: 'Lewati Agenda',
      };
    case 'delete':
    default:
      return {
        title: 'Hentikan Siklus Rencana?',
        desc: a.isIncome
          ? `Rencana pemasukan "${a.title}" senilai ${formatCurrencyRaw(a.amount)} dihentikan dan disimpan di arsip sebagai Dibatalkan.`
          : `Rencana "${a.title}" senilai ${formatCurrencyRaw(a.amount)} dihentikan dan disimpan di arsip sebagai Dibatalkan. Komitmen Dana Bebas yang aktif dilepas.`,
        icon: 'block',
        actionLabel: 'Hentikan Siklus',
      };
  }
}

function AgendaCard({
  agenda,
  onSkip,
  onScheduleFlexible,
  onStopCycle,
  onEdit,
  onFinish,
}: {
  agenda: AgendaItem;
  onSkip: () => void;
  onScheduleFlexible: () => void;
  onStopCycle: () => void;
  onEdit: () => void;
  onFinish: () => void;
}) {
  return (
    <article className={`p-4 sm:p-4.5 rounded-2xl border border-lo-border-hairline/80 transition-all duration-150 ${agenda.isIncome ? 'bg-lo-secondary-container/25' : 'bg-lo-warning/5'} hover:bg-lo-surface-recessed/70 hover:border-lo-border-hairline`}>
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="flex items-center gap-3.5 min-w-0">
           <div className={`w-10 h-10 rounded-2xl bg-white border border-lo-border-hairline flex items-center justify-center shrink-0 shadow-2xs ${agenda.isIncome ? 'text-lo-secondary' : 'text-lo-warning'}`}>
             <Icon name={agenda.isIncome ? 'south_west' : 'north_east'} className="text-[20px]" />
          </div>
          <div className="min-w-0 space-y-0.5">
            <div className="flex items-center gap-2 flex-wrap">
               <h2 className="font-headline font-semibold text-sm sm:text-base text-lo-text-ink truncate">
                 {agenda.title}
               </h2>
               <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold ${agenda.isIncome ? 'bg-lo-secondary/10 text-lo-secondary' : 'bg-lo-warning/10 text-lo-warning'}`}>{agenda.isIncome ? 'Pemasukan' : 'Pengeluaran'}</span>
              <span className={pillPassive}>{agenda.repeat}</span>
              {agenda.isOverdue ? <span className={pillWarning}>Terlambat</span> : null}
              {agenda.isDue && !agenda.isOverdue ? <span className={pillPassive}>Jatuh tempo hari ini</span> : null}
            </div>
            <div className="flex items-center gap-2 text-xs text-lo-text-subtle flex-wrap">
              <span className="flex items-center gap-1">
                <Icon name="calendar_today" className="text-[13px]" />
                <span>{agenda.displayDate}</span>
              </span>
              <span className="text-lo-text-subtle/60">·</span>
              <span className="truncate">{agenda.categoryLabel}</span>
            </div>
            {agenda.note ? <p className="text-xs text-lo-text-subtle truncate">{agenda.note}</p> : null}
            {agenda.defaultSourceAccountLabel ? (
              <p className="text-[11px] text-lo-text-subtle">Default: {agenda.defaultSourceAccountLabel}</p>
            ) : null}
          </div>
        </div>
        <div className="sm:text-right shrink-0 pl-13 sm:pl-0">
          <div
            className={`font-headline text-lg sm:text-[19px] font-medium tracking-tight ${
              agenda.isIncome ? 'text-lo-secondary' : 'text-lo-text-ink'
            }`}
          >
            {agenda.isIncome ? '+' : '−'}
            {formatCurrencyRaw(agenda.amount)}
          </div>
        </div>
      </div>
      <div className="pt-3 mt-3 border-t border-lo-border-hairline/60 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div className="flex items-center gap-2 text-xs flex-wrap">
          <button type="button" onClick={onEdit} className={btnGhost}>
            <Icon name="edit" className="text-[14px]" />
            <span>Edit</span>
          </button>
          <button type="button" onClick={onSkip} className={btnGhost}>
            <Icon name="skip_next" className="text-[14px]" />
            <span>Lewati</span>
          </button>
          {agenda.type === 'flexible' ? (
            <button
              type="button"
              onClick={onScheduleFlexible}
              className="px-3.5 py-1.5 rounded-full border border-lo-border-hairline bg-lo-surface-recessed hover:bg-lo-accent-wash text-lo-text-ink text-xs font-medium transition-all shadow-xs flex items-center gap-1.5 cursor-pointer"
            >
              <Icon name="event_upcoming" className="text-[14px] text-lo-text-subtle" />
              <span>Atur Tanggal</span>
            </button>
          ) : null}
          <button type="button" onClick={onStopCycle} className={btnDanger}>
            <Icon name="delete" className="text-[14px]" />
            <span>Hentikan</span>
          </button>
        </div>
        <div className="flex items-center self-end sm:self-auto">
          <button type="button" onClick={onFinish} className={btnPrimary}>
            <Icon
              name={agenda.isIncome ? 'done' : 'check_circle'}
              className="text-[15px]"
            />
            <span>{agenda.isIncome ? 'Sudah Masuk' : 'Bayar Sekarang'}</span>
          </button>
        </div>
      </div>
    </article>
  );
}

/* ── Realize modal ──
 * Rencana TIDAK terikat Sumber Dana. Akun hanya dipilih saat realizasi. */

function FinishAgendaModal({
  agenda,
  accounts,
  onClose,
  onFinish,
}: {
  agenda: AgendaItem;
  accounts: Account[];
  onClose: () => void;
  onFinish: (accountId: string) => Promise<boolean>;
}) {
  const [accountId, setAccountId] = useState(() =>
    accounts.some((account) => account.id === agenda.defaultSourceAccountId && !account.archived)
      ? agenda.defaultSourceAccountId ?? ''
      : ''
  );
  const [submitting, setSubmitting] = useState(false);

  const activeAccounts = useMemo(() => accounts.filter((a) => !a.archived), [accounts]);

  return (
    <Modal
      open
      onClose={onClose}
      title={`Selesaikan Rencana — ${agenda.title}`}
      subtitle="Pilih Sumber Dana aktual untuk mencatat uang masuk atau keluar."
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
            disabled={!accountId || submitting}
            onClick={async () => {
              setSubmitting(true);
              try {
                await onFinish(accountId);
              } finally {
                setSubmitting(false);
              }
            }}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="check" className="text-base" />
            <span>{agenda.isIncome ? 'Sudah Masuk' : 'Bayar Sekarang'}</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="p-4 rounded-xl bg-lo-surface-recessed border border-lo-border-hairline flex items-center justify-between">
          <span className="text-[11px] text-lo-text-subtle uppercase tracking-wide">
            Nominal Rencana
          </span>
          <span className="font-headline text-lg font-semibold tabular-nums text-lo-text-ink">
            {agenda.isIncome ? '+' : '−'}
            {formatCurrencyRaw(agenda.amount)}
          </span>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="finish-source-account">
            Sumber Dana <span className="text-lo-secondary">*</span>
          </label>
          <span className="text-[11px] text-lo-text-subtle">
            {agenda.isIncome
              ? 'Uang masuk ke rekening ini.'
              : 'Uang keluar dari rekening ini.'}
          </span>
          <select
            id="finish-source-account"
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

      </div>
    </Modal>
  );
}

function FlexiblePostponeModal({
  agenda,
  onClose,
  onPostpone,
}: {
  agenda: AgendaItem;
  onClose: () => void;
  onPostpone: (newDueDate: string) => Promise<boolean>;
}) {
  const today = todayIso();
  const [date, setDate] = useState('');
  const [submitting, setSubmitting] = useState(false);

  return (
    <Modal
      open
      onClose={onClose}
      title={`Atur Tanggal — ${agenda.title}`}
      subtitle="Rencana fleksibel belum memiliki tanggal. Pilih tanggal baru agar masuk ke agenda terjadwal."
      maxWidth="max-w-md"
      footer={
        <>
          <button type="button" onClick={onClose} className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink cursor-pointer">
            Batal
          </button>
          <button
            type="button"
            disabled={!date || date < today || submitting}
            onClick={async () => {
              setSubmitting(true);
              try {
                await onPostpone(date);
              } finally {
                setSubmitting(false);
              }
            }}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="event" className="text-base" />
            <span>Jadwalkan</span>
          </button>
        </>
      }
    >
      <label className="block text-xs font-medium text-lo-text-ink mb-1.5" htmlFor="flexible-agenda-date">
        Tanggal Mulai <span className="text-lo-secondary">*</span>
      </label>
      <input
        id="flexible-agenda-date"
        type="date"
        min={today}
        className={`${selectBase} text-xs`}
        value={date}
        onChange={(event) => setDate(event.target.value)}
      />
    </Modal>
  );
}

/* ── Create agenda modal ──
 * Rencana TIDAK terikat Sumber Dana — akun dipilih saat realizasi. */

function CreateAgendaModal({
  open,
  accounts,
  initialAgenda,
  onClose,
  onSave,
}: {
  open: boolean;
  accounts: Account[];
  initialAgenda?: AgendaItem;
  onClose: () => void;
  onSave: (input: CreateAgendaInput) => Promise<boolean>;
}) {
  const [isIncome, setIsIncome] = useState(initialAgenda?.isIncome ?? false);
  const [title, setTitle] = useState(initialAgenda?.title ?? '');
  const [amount, setAmount] = useState(initialAgenda
    ? String(initialAgenda.amount).replace(/\B(?=(\d{3})+(?!\d))/g, '.')
    : '');
  const [category, setCategory] = useState<string>(
    initialAgenda && !initialAgenda.isIncome && EXPENSE_CATEGORIES.some((categoryName) => categoryName === initialAgenda.categoryLabel)
      ? initialAgenda.categoryLabel
      : 'Makan & Minum'
  );
  const [date, setDate] = useState(initialAgenda?.rawDate ?? '');
  const [repeat, setRepeat] = useState(initialAgenda?.repeat ?? 'Satu Kali');
  const [agendaType, setAgendaType] = useState<'scheduled' | 'flexible'>(initialAgenda?.type ?? 'scheduled');
  const [note, setNote] = useState(initialAgenda?.note ?? '');
  const [defaultSourceAccountId, setDefaultSourceAccountId] = useState(() =>
    accounts.some((account) => account.id === initialAgenda?.defaultSourceAccountId && !account.archived)
      ? initialAgenda?.defaultSourceAccountId ?? ''
      : ''
  );
  const [submitting, setSubmitting] = useState(false);
  const today = todayIso();

  // Pengeluaran selalu terjadwal (tanggal wajib).
  // Pemasukan terjadwal: tanggal + siklus.
  // Pemasukan fleksibel: tanpa tanggal & siklus (waktunya belum pasti).
  const showScheduleKind = isIncome;
  const needsDate = !isIncome || agendaType === 'scheduled';
  const needsCycle = !isIncome || agendaType === 'scheduled';

  const amountNum = parseInt(amount.replace(/\D/g, ''), 10) || 0;
  const amountIsSafe = Number.isSafeInteger(amountNum);
  const dateIsInPast = needsDate && Boolean(date) && date < today && date !== initialAgenda?.rawDate;
  const canSubmit =
    title.trim().length > 0 &&
    amountNum > 0 &&
    amountIsSafe &&
    Boolean(defaultSourceAccountId) &&
    (!needsDate || (date.length > 0 && !dateIsInPast));

  const reset = () => {
    setIsIncome(false);
    setTitle('');
    setAmount('');
    setCategory('Makan & Minum');
    setDate('');
    setRepeat('Satu Kali');
    setAgendaType('scheduled');
    setNote('');
    setDefaultSourceAccountId('');
  };

  return (
    <Modal
      open={open}
      onClose={() => {
        reset();
        onClose();
      }}
       title={initialAgenda ? 'Edit Rencana Pengeluaran/Pemasukan' : 'Buat Rencana Pengeluaran/Pemasukan'}
      subtitle="Catat rencana sebelum uang benar-benar masuk atau keluar."
      maxWidth="max-w-lg"
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
            disabled={!canSubmit || submitting}
            onClick={async () => {
              setSubmitting(true);
              try {
                 const succeeded = await onSave({
                  title: title.trim(),
                  amount: amountNum,
                  isIncome,
                  rawDate: needsDate ? date : '',
                  categoryLabel: isIncome ? 'Pendapatan' : category,
                  repeat: needsCycle ? repeat : 'Satu Kali',
                  type: !isIncome ? 'scheduled' : agendaType,
                  defaultSourceAccountId,
                  note: note.trim() || undefined,
                });
                if (succeeded) reset();
              } finally {
                setSubmitting(false);
              }
            }}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="check" className="text-[15px]" />
            <span>{initialAgenda ? 'Simpan Perubahan' : 'Simpan'}</span>
          </button>
        </>
      }
    >
      <div className="space-y-4">
        <div>
          <div className="flex items-center justify-between mb-1.5">
            <span className="text-xs font-medium text-lo-text-ink">Jenis Rencana</span>
          </div>
          <div className="grid grid-cols-2 p-1 bg-lo-surface-recessed rounded-xl gap-1">
            <button
              type="button"
              onClick={() => {
                setIsIncome(false);
                setAgendaType('scheduled');
              }}
              className={`py-2 px-3 rounded-lg text-xs font-semibold flex items-center justify-center gap-1.5 transition-all ${
                !isIncome
                  ? 'bg-white text-lo-text-ink shadow-xs'
                  : 'text-lo-text-subtle hover:text-lo-text-ink'
              }`}
            >
              <Icon name="remove_circle_outline" className="text-[16px] text-lo-warning" />
              <span>Pengeluaran</span>
            </button>
            <button
              type="button"
              onClick={() => setIsIncome(true)}
              className={`py-2 px-3 rounded-lg text-xs font-semibold flex items-center justify-center gap-1.5 transition-all ${
                isIncome
                  ? 'bg-white text-lo-secondary shadow-xs'
                  : 'text-lo-text-subtle hover:text-lo-text-ink'
              }`}
            >
              <Icon name="add_circle_outline" className="text-[16px] text-lo-secondary" />
              <span>Pemasukan</span>
            </button>
          </div>
        </div>

        <div className="space-y-3">
          <div>
            <div className="flex items-center justify-between mb-1">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-title">
                Nama Rencana <span className="text-lo-secondary">*</span>
              </label>
            </div>
            <input
              id="agd-title"
              type="text"
              maxLength={256}
              className={inputBase}
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Contoh: Tagihan Internet, Gaji Pokok, Listrik..."
            />
          </div>
          <div>
            <div className="flex items-center justify-between mb-1">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-amount">
                Jumlah (Rp)<span className="text-lo-secondary">*</span>
              </label>
            </div>
            <div className="relative">
              <span className="absolute left-3.5 top-1/2 -translate-y-1/2 text-xs font-medium text-lo-text-subtle">
                Rp
              </span>
              <input
                id="agd-amount"
                type="text"
                inputMode="numeric"
                aria-invalid={amount.length > 0 && !amountIsSafe}
                className={`${inputBase} pl-10 font-semibold tabular-nums`}
                value={amount}
                onChange={(e) =>
                  setAmount(e.target.value.replace(/\D/g, '').replace(/\B(?=(\d{3})+(?!\d))/g, '.'))
                }
                placeholder="0"
              />
            </div>
            {amount.length > 0 && !amountIsSafe ? (
              <p role="alert" className="text-[11px] text-lo-error mt-1">
                Nominal terlalu besar. Masukkan nominal hingga Rp9.007.199.254.740.991.
              </p>
            ) : null}
          </div>
        </div>

        {needsDate || needsCycle ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            {needsDate ? (
              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-date">
                    Tanggal Mulai<span className="text-lo-secondary">*</span>
                  </label>
                </div>
                <input
                  id="agd-date"
                  type="date"
                  min={today}
                  aria-invalid={dateIsInPast}
                  className={`${selectBase} text-xs`}
                  value={date}
                  onChange={(e) => setDate(e.target.value)}
                />
                {dateIsInPast ? (
                  <p role="alert" className="text-[11px] text-lo-error mt-1">
                    Tanggal Mulai tidak boleh sebelum hari ini.
                  </p>
                ) : null}
              </div>
            ) : null}
            {needsCycle ? (
              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-repeat">
                    Siklus
                  </label>
                </div>
                <select
                  id="agd-repeat"
                  className={`${selectBase} text-xs`}
                  value={repeat}
                  onChange={(e) => setRepeat(e.target.value)}
                >
                  <option value="Bulanan">Bulanan</option>
                  <option value="Mingguan">Mingguan</option>
                  <option value="Per 3 Bulan">Per 3 Bulan</option>
                  <option value="Tahunan">Tahunan</option>
                  <option value="Satu Kali">Satu Kali</option>
                </select>
              </div>
            ) : null}
          </div>
        ) : null}

        {showScheduleKind ? (
          <div>
            <div className="flex items-center justify-between mb-1">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-type">
                Jenis Jadwal
              </label>
            </div>
            <select
              id="agd-type"
              className={`${selectBase} text-xs`}
              value={agendaType}
              onChange={(e) => setAgendaType(e.target.value as 'scheduled' | 'flexible')}
            >
              <option value="scheduled">Terjadwal</option>
              <option value="flexible">Fleksibel</option>
            </select>
          </div>
        ) : null}

        {!isIncome ? (
          <div>
            <label className="block text-xs font-medium text-lo-text-ink mb-1.5" htmlFor="agd-category">
              Kategori Pengeluaran <span className="text-lo-secondary">*</span>
            </label>
            <select
              id="agd-category"
              className={`${selectBase} text-xs`}
              value={category}
              onChange={(e) => setCategory(e.target.value)}
            >
              {EXPENSE_CATEGORIES.map((transactionCategory) => (
                <option key={transactionCategory} value={transactionCategory}>
                  {transactionCategory}
                </option>
              ))}
            </select>
          </div>
        ) : null}

        <div className="flex flex-col gap-1.5">
          <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-default-account">
            Sumber Dana Default <span className="text-lo-secondary">*</span>
          </label>
          <select
            id="agd-default-account"
            className={selectBase}
            value={defaultSourceAccountId}
            onChange={(e) => setDefaultSourceAccountId(e.target.value)}
          >
            <option value="" disabled>Pilih Sumber Dana Default…</option>
            {accounts.filter((account) => !account.archived).map((account) => (
              <option key={account.id} value={account.id}>{account.name}</option>
            ))}
          </select>
          {accounts.every((account) => account.archived) ? (
            <p className="text-[11px] text-lo-error">Tambahkan Sumber Dana aktif sebelum membuat rencana.</p>
          ) : null}
        </div>

        <div>
          <div className="flex items-center justify-between mb-1">
            <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-note">
              Catatan
            </label>
            <span className="text-[11px] text-lo-text-subtle italic">opsional</span>
          </div>
          <input
            id="agd-note"
            type="text"
            maxLength={512}
            className={`${inputBaseSm} text-xs`}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Contoh: No. Pelanggan 019284 / Invoice termin 1"
          />
        </div>

        <div className="pt-3 border-t border-lo-border-hairline/70 text-[11px] leading-relaxed text-lo-text-subtle space-y-1">
          {isIncome ? (
            <p>Rencana pemasukan tidak mengurangi Dana Bebas. Sumber Dana Default hanya saran dan bisa diganti saat pemasukan dicatat.</p>
          ) : repeat === 'Satu Kali' ? (
            <p>Rencana Satu Kali langsung mengikat Dana Bebas saat disimpan, meskipun tanggal kejadian masih mendatang. Mengubah tanggal kejadian tidak mengubah komitmen ini.</p>
          ) : (
            <p>Untuk rencana berulang, Dana Bebas terikat sejak awal periode kalender: Senin untuk mingguan, tanggal 1 untuk bulanan, awal kuartal untuk per 3 bulan, dan 1 Januari untuk tahunan. Tanggal di atas tetap menunjukkan tanggal kejadiannya.</p>
          )}
          {!isIncome ? <p>Sumber Dana Default hanya saran dan tetap bisa diganti saat rencana dibayar.</p> : null}
        </div>
      </div>
    </Modal>
  );
}

function ArchiveModal({
  open,
  onClose,
  items,
}: {
  open: boolean;
  onClose: () => void;
  items: ArchivedAgenda[];
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Arsip Agenda Kas"
      subtitle="Riwayat agenda yang telah diselesaikan, dilewati, atau dibatalkan."
      maxWidth="max-w-lg"
      footer={
        <button
          type="button"
          onClick={onClose}
          className="px-5 py-2 rounded-full bg-lo-surface-recessed text-lo-text-ink hover:bg-lo-border-hairline/60 text-xs font-medium cursor-pointer transition-colors"
        >
          Tutup Arsip
        </button>
      }
    >
      <div className="space-y-2 max-h-[50vh] overflow-y-auto pr-1">
        {items.length === 0 ? (
          <p className="text-xs text-lo-text-subtle py-6 text-center">
            Belum ada agenda yang diarsipkan.
          </p>
        ) : (
          items.map((item) => (
            <div
              key={item.id}
              className="p-3 rounded-xl bg-lo-surface-recessed/60 border border-lo-border-hairline flex items-center justify-between gap-3"
            >
              <div className="min-w-0">
                <p className="text-xs font-medium text-lo-text-ink truncate">{item.title}</p>
                <p className="text-[11px] text-lo-text-subtle mt-0.5">{item.date}</p>
                {item.sourceAccountLabel ? (
                  <p className="text-[11px] text-lo-text-subtle mt-0.5">
                    Sumber Dana {item.sourceAccountKind === 'actual' ? 'Aktual' : 'Default'}: {item.sourceAccountLabel}
                  </p>
                ) : null}
              </div>
              <div className="text-right shrink-0">
                <p className={`text-xs font-semibold tabular-nums ${item.isIncome ? 'text-lo-secondary' : 'text-lo-text-ink'}`}>
                  {item.isIncome ? '+' : '−'}{formatCurrencyRaw(item.amount)}
                </p>
                <span className="inline-block px-2 py-0.5 rounded-full bg-lo-accent-wash text-lo-secondary text-[10px] font-medium mt-0.5">
                  {item.status}
                </span>
              </div>
            </div>
          ))
        )}
      </div>
    </Modal>
  );
}
