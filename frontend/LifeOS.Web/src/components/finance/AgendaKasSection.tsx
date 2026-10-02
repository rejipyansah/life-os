import { useMemo, useState } from 'react';

import {
  formatCurrencyRaw,
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
  selectBase,
} from './shared';

const AGENDA_PER_PAGE = 5;

interface AgendaKasSectionProps {
  accounts: Account[];
  agendas: AgendaItem[];
  archivedAgendas: ArchivedAgenda[];
  filter: 'all' | 'scheduled' | 'flexible';
  page: number;
  onFilterChange: (f: 'all' | 'scheduled' | 'flexible') => void;
  onPageChange: (p: number) => void;
  onSkip: (id: string) => void;
  onPostpone: (id: string) => void;
  onDelete: (id: string) => void;
  onFinish: (id: string) => void;
  onCreate: (input: CreateAgendaInput) => void;
}

type ConfirmKind = 'skip' | 'postpone' | 'delete' | 'finish';

export default function AgendaKasSection({
  accounts,
  agendas,
  archivedAgendas,
  filter,
  page,
  onFilterChange,
  onPageChange,
  onSkip,
  onPostpone,
  onDelete,
  onFinish,
  onCreate,
}: AgendaKasSectionProps) {
  const [createOpen, setCreateOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [confirm, setConfirm] = useState<{ kind: ConfirmKind; agenda: AgendaItem } | null>(
    null
  );

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
            onClick={() => setCreateOpen(true)}
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
          <span>Lihat rencana yang Selesai</span>
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
              onPostpone={() => setConfirm({ kind: 'postpone', agenda: a })}
              onDelete={() => setConfirm({ kind: 'delete', agenda: a })}
              onFinish={() => setConfirm({ kind: 'finish', agenda: a })}
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
        open={createOpen}
        accounts={accounts}
        onClose={() => setCreateOpen(false)}
        onCreate={(input) => {
          onCreate(input);
          setCreateOpen(false);
        }}
      />

      <ArchiveModal
        open={archiveOpen}
        onClose={() => setArchiveOpen(false)}
        items={archivedAgendas}
      />

      {confirm && confirmConfig ? (
        <ConfirmDialog
          open
          onClose={() => setConfirm(null)}
          onConfirm={() => {
            const id = confirm.agenda.id;
            if (confirm.kind === 'skip') onSkip(id);
            else if (confirm.kind === 'postpone') onPostpone(id);
            else if (confirm.kind === 'delete') onDelete(id);
            else onFinish(id);
          }}
          title={confirmConfig.title}
          description={confirmConfig.desc}
          icon={confirmConfig.icon}
          actionLabel={confirmConfig.actionLabel}
          detailTitle={confirm.agenda.title}
          detailAmount={`${confirm.agenda.isIncome ? '+' : '-'} ${formatCurrencyRaw(confirm.agenda.amount)}`}
          detailAccount={`${confirm.agenda.accountLabel} · ${confirm.agenda.displayDate}`}
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
        desc: `Agenda "${a.title}" tidak akan dieksekusi pada jadwal ini dan langsung dialihkan ke arsip.`,
        icon: 'redo',
        actionLabel: 'Lewati Agenda',
      };
    case 'postpone':
      return {
        title: 'Tunda Agenda ke Besok?',
        desc: `Tanggal jatuh tempo "${a.title}" akan digeser ke hari berikutnya tanpa mengubah data lainnya.`,
        icon: 'update',
        actionLabel: 'Tunda ke Besok',
      };
    case 'delete':
      return {
        title: 'Hapus Agenda Rencana?',
        desc: `Rencana "${a.title}" senilai ${formatCurrencyRaw(a.amount)} akan dihapus permanen dari daftar aktif.`,
        icon: 'delete',
        actionLabel: 'Hapus Agenda',
      };
    case 'finish':
    default:
      return a.isIncome
        ? {
            title: 'Konfirmasi Dana Masuk?',
            desc: `Tandai penerimaan dana ${formatCurrencyRaw(a.amount)} dari "${a.title}" telah diterima di ${a.accountLabel}.`,
            icon: 'payments',
            actionLabel: 'Sudah Masuk',
          }
        : {
            title: 'Konfirmasi Pembayaran Kas?',
            desc: `Tandai pembayaran kas ${formatCurrencyRaw(a.amount)} untuk "${a.title}" telah diselesaikan melalui ${a.accountLabel}.`,
            icon: 'check_circle',
            actionLabel: 'Bayar Sekarang',
          };
  }
}

function AgendaCard({
  agenda,
  onSkip,
  onPostpone,
  onDelete,
  onFinish,
}: {
  agenda: AgendaItem;
  onSkip: () => void;
  onPostpone: () => void;
  onDelete: () => void;
  onFinish: () => void;
}) {
  return (
    <article className="p-4 sm:p-4.5 rounded-2xl bg-lo-surface-recessed/40 hover:bg-lo-surface-recessed/70 border border-lo-border-hairline/80 hover:border-lo-border-hairline transition-all duration-150">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="flex items-center gap-3.5 min-w-0">
          <div className="w-10 h-10 rounded-2xl bg-white border border-lo-border-hairline flex items-center justify-center shrink-0 shadow-2xs text-lo-text-ink">
            <Icon name={agenda.icon} className="text-[20px] text-lo-secondary" />
          </div>
          <div className="min-w-0 space-y-0.5">
            <div className="flex items-center gap-2 flex-wrap">
              <h2 className="font-headline font-semibold text-sm sm:text-base text-lo-text-ink truncate">
                {agenda.title}
              </h2>
              <span className={pillPassive}>{agenda.repeat}</span>
            </div>
            <div className="flex items-center gap-2 text-xs text-lo-text-subtle">
              <span className="flex items-center gap-1">
                <Icon name="calendar_today" className="text-[13px]" />
                <span>{agenda.displayDate}</span>
              </span>
              <span className="text-lo-text-subtle/60">·</span>
              <span className="truncate">{agenda.accountLabel}</span>
            </div>
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
          <div className="text-[11px] text-lo-text-subtle/80">{agenda.categoryLabel}</div>
        </div>
      </div>
      <div className="pt-3 mt-3 border-t border-lo-border-hairline/60 flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-xs flex-wrap">
          <button type="button" onClick={onSkip} className={btnGhost}>
            <Icon name="skip_next" className="text-[14px]" />
            <span>Lewati</span>
          </button>
          <button
            type="button"
            onClick={onPostpone}
            className="px-3.5 py-1.5 rounded-full border border-lo-border-hairline bg-lo-surface-recessed hover:bg-lo-accent-wash text-lo-text-ink text-xs font-medium transition-all shadow-xs flex items-center gap-1.5 cursor-pointer"
          >
            <Icon name="event_upcoming" className="text-[14px] text-lo-text-subtle" />
            <span>Tunda Besok</span>
          </button>
          <button type="button" onClick={onDelete} className={btnDanger}>
            <Icon name="delete" className="text-[14px]" />
            <span>Hapus</span>
          </button>
        </div>
        <div className="flex items-center">
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

function CreateAgendaModal({
  open,
  accounts,
  onClose,
  onCreate,
}: {
  open: boolean;
  accounts: Account[];
  onClose: () => void;
  onCreate: (input: CreateAgendaInput) => void;
}) {
  const [isIncome, setIsIncome] = useState(false);
  const [title, setTitle] = useState('');
  const [amount, setAmount] = useState('');
  const [category, setCategory] = useState('Utilitas Rutin');
  const [date, setDate] = useState('');
  const [repeat, setRepeat] = useState('Bulanan');
  const [accountLabel, setAccountLabel] = useState('');
  const [agendaType, setAgendaType] = useState<'scheduled' | 'flexible'>('scheduled');
  const [note, setNote] = useState('');

  // Only real, non-archived accounts may be picked as the cash account.
  const selectableAccounts = accounts.filter((a) => !a.archived);
  const selectedAccount =
    selectableAccounts.find((a) => a.name === accountLabel) ?? selectableAccounts[0];

  // Pengeluaran selalu terjadwal (tanggal wajib).
  // Pemasukan terjadwal: tanggal + siklus.
  // Pemasukan fleksibel: tanpa tanggal & siklus (waktunya belum pasti).
  const showScheduleKind = isIncome;
  const needsDate = !isIncome || agendaType === 'scheduled';
  const needsCycle = !isIncome || agendaType === 'scheduled';

  const amountNum = parseInt(amount.replace(/\D/g, ''), 10) || 0;
  const canSubmit =
    title.trim().length > 0 &&
    amountNum > 0 &&
    (!needsDate || date.length > 0) &&
    !!selectedAccount;

  const reset = () => {
    setIsIncome(false);
    setTitle('');
    setAmount('');
    setCategory('Utilitas Rutin');
    setDate('');
    setRepeat('Bulanan');
    setAccountLabel('');
    setAgendaType('scheduled');
    setNote('');
  };

  return (
    <Modal
      open={open}
      onClose={() => {
        reset();
        onClose();
      }}
      title="Buat Rencana Pengeluaran/Pemasukan"
      subtitle="Atur penerimaan atau pembayaran yang akan datang."
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
            disabled={!canSubmit}
            onClick={() => {
              onCreate({
                title: title.trim(),
                amount: amountNum,
                isIncome,
                rawDate: needsDate ? date : '',
                accountLabel: selectedAccount?.name ?? '',
                categoryLabel: isIncome ? 'Pemasukan Kas' : category,
                repeat: needsCycle ? repeat : 'Satu Kali',
                type: !isIncome ? 'scheduled' : agendaType,
                note: note.trim() || undefined,
              });
              reset();
            }}
            className={`${btnPrimary} disabled:opacity-40`}
          >
            <Icon name="check" className="text-[15px]" />
            <span>Simpan</span>
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
                className={`${inputBase} pl-10 font-semibold tabular-nums`}
                value={amount}
                onChange={(e) =>
                  setAmount(e.target.value.replace(/\D/g, '').replace(/\B(?=(\d{3})+(?!\d))/g, '.'))
                }
                placeholder="0"
              />
            </div>
          </div>
        </div>

        {!isIncome ? (
          <div>
            <div className="flex items-center justify-between mb-1">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-category">
                Kategori Pengeluaran<span className="text-lo-secondary">*</span>
              </label>
            </div>
            <select
              id="agd-category"
              className={`${selectBase} text-xs`}
              value={category}
              onChange={(e) => setCategory(e.target.value)}
            >
              <option value="Utilitas Rutin">Utilitas Rutin</option>
              <option value="Tagihan Internet">Tagihan Internet</option>
              <option value="Belanja Dapur">Belanja Dapur</option>
              <option value="Asuransi & Kewajiban">Asuransi &amp; Kewajiban</option>
              <option value="Transportasi">Transportasi</option>
              <option value="Hiburan Digital">Hiburan Digital</option>
              <option value="Lainnya">Lainnya</option>
            </select>
          </div>
        ) : null}

        {needsDate || needsCycle ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            {needsDate ? (
              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-date">
                    Tanggal<span className="text-lo-secondary">*</span>
                  </label>
                </div>
                <input
                  id="agd-date"
                  type="date"
                  className={`${selectBase} text-xs`}
                  value={date}
                  onChange={(e) => setDate(e.target.value)}
                />
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
                  <option value="Tahunan">Tahunan</option>
                  <option value="Satu Kali">Satu Kali</option>
                </select>
              </div>
            ) : null}
          </div>
        ) : null}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <div className="flex items-center justify-between mb-1">
              <label className="text-xs font-medium text-lo-text-ink" htmlFor="agd-account">
                Sumber Dana
              </label>
            </div>
            <select
              id="agd-account"
              className={`${selectBase} text-xs`}
              value={selectedAccount?.name ?? ''}
              onChange={(e) => setAccountLabel(e.target.value)}
            >
              {selectableAccounts.length === 0 && (
                <option value="" disabled>
                  Belum ada sumber dana
                </option>
              )}
              {selectableAccounts.map((a) => (
                <option key={a.id} value={a.name}>
                  {a.name}
                </option>
              ))}
            </select>
          </div>
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
            className={`${inputBaseSm} text-xs`}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Contoh: No. Pelanggan 019284 / Invoice termin 1"
          />
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
      subtitle="Riwayat agenda yang telah diselesaikan atau dilewati."
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
                <p className="text-[11px] text-lo-text-subtle mt-0.5">
                  {item.date} · {item.accountLabel}
                </p>
              </div>
              <div className="text-right shrink-0">
                <p className="text-xs font-semibold text-lo-text-ink tabular-nums">
                  {formatCurrencyRaw(item.amount)}
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
