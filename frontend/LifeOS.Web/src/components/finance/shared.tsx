import type { ReactNode } from 'react';
import { useEffect } from 'react';

import type { FinanceToast } from '../../finance';

/* ── Shared class tokens (from DESIGN.md + module sources) ── */

export const btnPrimary =
  'px-4 py-2 rounded-full bg-lo-primary hover:bg-lo-primary-hover text-white text-xs font-semibold shadow-xs transition-all cursor-pointer active:scale-95 inline-flex items-center gap-1.5 disabled:opacity-50 disabled:cursor-not-allowed shrink-0';

export const btnSecondary =
  'px-3.5 py-2 rounded-full bg-lo-surface-recessed hover:bg-lo-border-hairline/60 text-lo-text-ink text-xs font-medium transition-all flex items-center gap-1.5 cursor-pointer border border-lo-border-hairline active:scale-95 shrink-0';

export const btnGhost =
  'px-3 py-1.5 rounded-full hover:bg-white border border-lo-border-hairline text-lo-text-subtle hover:text-lo-text-ink text-xs font-medium transition-colors cursor-pointer inline-flex items-center gap-1.5 shrink-0';

export const btnDanger =
  'px-3 py-1.5 rounded-full border border-red-200/80 bg-red-50/60 hover:bg-red-100/70 text-lo-error hover:text-red-700 text-xs font-medium transition-all shadow-xs flex items-center gap-1.5 cursor-pointer shrink-0';

export const btnDangerSolid =
  'px-5 py-2 rounded-full bg-lo-error hover:bg-red-700 text-white text-xs font-semibold shadow-xs transition-all cursor-pointer inline-flex items-center gap-1.5 active:scale-95 shrink-0';

export const cardBase =
  'bg-white rounded-2xl border border-lo-border-hairline shadow-xs';

export const inputBase =
  'w-full px-3.5 py-2.5 rounded-xl bg-lo-surface-recessed border border-lo-border-hairline text-sm text-lo-text-ink placeholder:text-lo-text-subtle/50 focus:outline-none focus:bg-white focus:ring-1 focus:ring-lo-primary focus:border-lo-primary transition-all';

export const inputBaseSm =
  'w-full px-3 py-2 rounded-xl bg-lo-surface-recessed border border-lo-border-hairline text-xs text-lo-text-ink placeholder:text-lo-text-subtle/50 focus:outline-none focus:bg-white focus:ring-1 focus:ring-lo-primary focus:border-lo-primary transition-all';

export const selectBase =
  'w-full px-3 py-2 rounded-xl bg-lo-surface-recessed border border-lo-border-hairline text-xs text-lo-text-ink focus:outline-none focus:bg-white focus:ring-1 focus:ring-lo-primary shadow-xs cursor-pointer transition-all';

export const pillPassive =
  'px-2.5 py-0.5 rounded-full bg-lo-surface-recessed text-lo-text-subtle text-[11px] font-medium border border-lo-border-hairline/60 shrink-0';

export const pillAccent =
  'px-2.5 py-0.5 rounded-full bg-lo-accent-wash text-lo-secondary text-[11px] font-medium border border-lo-secondary/20 shrink-0';

export const pillWarning =
  'text-[10px] font-semibold text-lo-warning bg-lo-warning-soft px-2 py-0.5 rounded-full border border-lo-warning/20 shrink-0';

export const sectionLabel =
  'text-[11px] uppercase tracking-wider font-semibold text-lo-text-subtle';

export const moneyClass =
  'font-headline tabular-nums tracking-tight text-lo-text-ink';

/* ── Material icon helper ── */

export function Icon({
  name,
  className = 'text-[18px]',
}: {
  name: string;
  className?: string;
}) {
  return (
    <span className={`material-symbols-outlined ${className}`} aria-hidden="true">
      {name}
    </span>
  );
}

/* ── Modal ── */

interface ModalProps {
  open: boolean;
  onClose: () => void;
  title: string;
  subtitle?: string;
  icon?: string;
  children: ReactNode;
  footer?: ReactNode;
  maxWidth?: string;
  hideHeaderBorder?: boolean;
}

export function Modal({
  open,
  onClose,
  title,
  subtitle,
  icon,
  children,
  footer,
  maxWidth = 'max-w-md',
  hideHeaderBorder = false,
}: ModalProps) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 bg-lo-text-ink/35 backdrop-blur-[2px] flex items-center justify-center p-4"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-label={title}
    >
      <div
        className={`bg-lo-surface-cream ${maxWidth} w-full rounded-2xl p-6 shadow-xl border border-lo-border-hairline max-h-[90vh] overflow-y-auto animate-[fadeIn_0.2s_cubic-bezier(0.16,1,0.3,1)]`}
        onClick={(e) => e.stopPropagation()}
      >
        <div
          className={`flex items-start justify-between gap-3 ${
            hideHeaderBorder ? 'pb-1' : 'pb-4 border-b border-lo-border-hairline'
          }`}
        >
          <div className="space-y-1 min-w-0">
            {icon ? (
              <div className="flex items-center gap-2">
                <Icon name={icon} className="text-lo-secondary text-xl" />
                <h3 className="font-headline text-lg font-medium text-lo-text-ink">
                  {title}
                </h3>
              </div>
            ) : (
              <h3 className="font-headline text-lg font-medium text-lo-text-ink">
                {title}
              </h3>
            )}
            {subtitle ? (
              <p className="text-xs text-lo-text-subtle">{subtitle}</p>
            ) : null}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Tutup modal"
            className="w-8 h-8 rounded-full bg-lo-surface-recessed hover:bg-lo-border-hairline flex items-center justify-center text-lo-text-subtle hover:text-lo-text-ink transition-colors cursor-pointer border border-lo-border-hairline/60 shrink-0"
          >
            <Icon name="close" className="text-base" />
          </button>
        </div>
        <div className="pt-4">{children}</div>
        {footer ? (
          <div className="pt-4 mt-4 border-t border-lo-border-hairline flex items-center justify-end gap-2.5">
            {footer}
          </div>
        ) : null}
      </div>
    </div>
  );
}

/* ── Toast viewport ── */

export function ToastViewport({
  toasts,
  onDismiss,
}: {
  toasts: FinanceToast[];
  onDismiss: (id: number) => void;
}) {
  if (toasts.length === 0) return null;
  return (
    <div className="fixed bottom-6 right-6 z-[60] flex flex-col gap-2 pointer-events-none">
      {toasts.map((t) => (
        <div
          key={t.id}
          className="px-4 py-2.5 rounded-xl bg-white text-lo-text-ink border border-lo-border-hairline shadow-lg text-xs font-medium flex items-center gap-2 pointer-events-auto animate-[fadeIn_0.25s_cubic-bezier(0.16,1,0.3,1)] max-w-xs"
        >
          <Icon name={t.icon || 'check_circle'} className="text-lo-secondary text-[18px]" />
          <span>{t.message}</span>
          <button
            type="button"
            onClick={() => onDismiss(t.id)}
            className="ml-1 text-lo-text-subtle hover:text-lo-text-ink cursor-pointer"
            aria-label="Tutup notifikasi"
          >
            <Icon name="close" className="text-sm" />
          </button>
        </div>
      ))}
    </div>
  );
}

/* ── Confirm dialog ── */

interface ConfirmDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  title: string;
  description: string;
  icon?: string;
  actionLabel: string;
  detailTitle?: string;
  detailAmount?: string;
  detailAccount?: string;
  danger?: boolean;
}

export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  title,
  description,
  icon = 'help_outline',
  actionLabel,
  detailTitle,
  detailAmount,
  detailAccount,
  danger = false,
}: ConfirmDialogProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      maxWidth="max-w-md"
      hideHeaderBorder
    >
      <div className="space-y-4">
        <div className="flex items-start gap-3.5">
          <div
            className={`w-10 h-10 rounded-2xl flex items-center justify-center shrink-0 border ${
              danger
                ? 'bg-red-50 text-lo-error border-red-200'
                : 'bg-lo-accent-wash text-lo-secondary border-lo-border-hairline/60'
            }`}
          >
            <Icon name={icon} className="text-[20px]" />
          </div>
          <p className="text-xs text-lo-text-subtle leading-relaxed flex-1">
            {description}
          </p>
        </div>
        {detailTitle || detailAmount || detailAccount ? (
          <div className="p-3.5 rounded-2xl bg-lo-surface-recessed border border-lo-border-hairline text-xs space-y-2">
            {detailTitle ? (
              <div className="flex justify-between items-center text-lo-text-subtle">
                <span>Agenda</span>
                <span className="font-medium text-lo-text-ink text-right">
                  {detailTitle}
                </span>
              </div>
            ) : null}
            {detailAmount ? (
              <div className="flex justify-between items-center text-lo-text-subtle">
                <span>Nominal</span>
                <span className="font-headline font-semibold text-lo-text-ink text-right">
                  {detailAmount}
                </span>
              </div>
            ) : null}
            {detailAccount ? (
              <div className="flex justify-between items-center text-lo-text-subtle">
                <span>Rekening &amp; Tanggal</span>
                <span className="text-lo-text-ink text-right">{detailAccount}</span>
              </div>
            ) : null}
          </div>
        ) : null}
        <div className="flex items-center justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-full text-xs text-lo-text-subtle hover:text-lo-text-ink cursor-pointer transition-colors"
          >
            Batal
          </button>
          <button
            type="button"
            onClick={() => {
              onConfirm();
              onClose();
            }}
            className={`px-5 py-2 rounded-full text-xs font-medium text-white cursor-pointer transition-all shadow-sm flex items-center gap-1.5 ${
              danger
                ? 'bg-lo-error hover:bg-red-700'
                : 'bg-lo-primary hover:bg-lo-primary-hover'
            }`}
          >
            <Icon name={danger ? 'delete' : 'check'} className="text-[15px]" />
            <span>{actionLabel}</span>
          </button>
        </div>
      </div>
    </Modal>
  );
}

/* ── Empty state ── */

export function EmptyState({
  icon,
  title,
  hint,
}: {
  icon: string;
  title: string;
  hint?: string;
}) {
  return (
    <div className="py-12 px-4 text-center rounded-2xl border border-dashed border-lo-border-hairline text-lo-text-subtle text-xs space-y-1.5">
      <Icon name={icon} className="text-[32px] text-lo-text-subtle/50 block mb-1" />
      <p className="font-medium text-lo-text-ink text-sm">{title}</p>
      {hint ? <p className="text-lo-text-subtle/80">{hint}</p> : null}
    </div>
  );
}

/* ── Pagination ── */

export function Pagination({
  page,
  totalPages,
  totalItems,
  infoLabel,
  onChange,
  labels,
}: {
  page: number;
  totalPages: number;
  totalItems: number;
  infoLabel: (from: number, to: number, total: number) => ReactNode;
  onChange: (p: number) => void;
  labels?: { prev: string; next: string };
}) {
  if (totalItems <= 0) return null;
  const show = totalPages > 1;
  const from = totalItems === 0 ? 0 : (page - 1) * 5 + 1;
  const to = Math.min(page * 5, totalItems);

  return (
    <div className="flex flex-col sm:flex-row items-center justify-between gap-3 pt-4 mt-4 border-t border-lo-border-hairline/60 text-xs">
      <div className="text-lo-text-subtle order-2 sm:order-1 text-center sm:text-left">
        {infoLabel(from, to, totalItems)}
      </div>
      {show ? (
        <div className="flex items-center gap-1.5 order-1 sm:order-2">
          <button
            type="button"
            disabled={page <= 1}
            onClick={() => onChange(page - 1)}
            className="p-2 rounded-xl text-lo-text-ink hover:bg-lo-surface-recessed disabled:opacity-35 disabled:hover:bg-transparent disabled:cursor-not-allowed transition-all inline-flex items-center justify-center cursor-pointer border border-lo-border-hairline/50"
          >
            <Icon name="chevron_left" className="text-base" />
            {labels?.prev ? (
              <span className="text-xs ml-0.5 hidden sm:inline">{labels.prev}</span>
            ) : null}
          </button>
          <div className="flex items-center gap-1">
            {Array.from({ length: totalPages }, (_, i) => i + 1).map((n) => (
              <button
                key={n}
                type="button"
                onClick={() => onChange(n)}
                className={`w-8 h-8 rounded-full text-[11px] font-semibold transition-all cursor-pointer flex items-center justify-center ${
                  n === page
                    ? 'bg-lo-secondary text-lo-surface-cream shadow-xs'
                    : 'bg-lo-surface-recessed text-lo-text-subtle hover:text-lo-text-ink hover:bg-lo-container-low'
                }`}
              >
                {n}
              </button>
            ))}
          </div>
          <button
            type="button"
            disabled={page >= totalPages}
            onClick={() => onChange(page + 1)}
            className="p-2 rounded-xl text-lo-text-ink hover:bg-lo-surface-recessed disabled:opacity-35 disabled:hover:bg-transparent disabled:cursor-not-allowed transition-all inline-flex items-center justify-center cursor-pointer border border-lo-border-hairline/50"
          >
            {labels?.next ? (
              <span className="text-xs mr-0.5 hidden sm:inline">{labels.next}</span>
            ) : null}
            <Icon name="chevron_right" className="text-base" />
          </button>
        </div>
      ) : (
        <div className="order-1 sm:order-2 text-lo-text-subtle">
          {infoLabel(from, to, totalItems)}
        </div>
      )}
    </div>
  );
}

/* ── Progress bar ── */

export function ProgressBar({
  percent,
  className = '',
}: {
  percent: number;
  className?: string;
}) {
  const clamped = Math.max(0, Math.min(100, percent));
  return (
    <div className={`w-full h-1.5 rounded-full bg-lo-surface-recessed overflow-hidden ${className}`}>
      <div
        className="h-full rounded-full bg-lo-secondary transition-all duration-300"
        style={{ width: `${clamped}%` }}
      />
    </div>
  );
}
