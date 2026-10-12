import { useState } from 'react';

import { useFinanceState } from '../../finance';
import Navigation from '../Navigation';
import AppFooter from '../AppFooter';
import type { NavDestination } from '../Home/navItems';
import { ToastViewport } from './shared';
import { UangBebasCard, JatuhTempoCard, FormulaModal } from './UangBebasSection';
import CatatTransaksiSection from './CatatTransaksiSection';
import YangDisisihkanSection from './YangDisisihkanSection';
import SumberDanaSection from './SumberDanaSection';
import AgendaKasSection from './AgendaKasSection';
import AktivitasTerkiniSection from './AktivitasTerkiniSection';

interface FinancePageProps {
  onBackToHome?: () => void;
  onRequestLogin?: () => void;
  onNavigate?: (dest: NavDestination) => void;
  isGuest?: boolean;
  onLogout?: () => void;
}

export default function FinancePage({
  onBackToHome,
  onRequestLogin,
  onNavigate,
  isGuest,
  onLogout,
}: FinancePageProps) {
  const state = useFinanceState();
  const [formulaOpen, setFormulaOpen] = useState(false);

  const handleNavigate = (dest: NavDestination) => {
    if (dest === 'beranda' && onBackToHome) {
      onBackToHome();
    } else if (onNavigate) {
      onNavigate(dest);
    }
  };

  return (
    <div className="flex-1 flex flex-col bg-lo-surface font-body text-lo-text-ink">
      <Navigation
        active="keuangan"
        onNavigate={handleNavigate}
        showLogout={!isGuest}
        onLogout={onLogout}
        showLogin={isGuest}
        onLogin={onRequestLogin}
      />

      <main className="min-w-0 flex-1 w-full max-w-7xl mx-auto px-3 min-[360px]:px-4 sm:px-6 lg:px-8 pt-16 md:pt-20 pb-[calc(5rem+env(safe-area-inset-bottom))] md:pb-16">
        {state.loadError ? (
          <div
            className="mb-5 flex flex-col gap-3 rounded-xl border border-lo-error/30 bg-lo-error-container px-4 py-3 text-sm text-lo-error sm:flex-row sm:items-center sm:justify-between"
            role="alert"
          >
            <span>{state.loadError}</span>
            <button
              type="button"
              onClick={state.retry}
              disabled={state.isLoading}
              className="self-start rounded-full border border-lo-error/30 px-4 py-2 font-medium transition-colors hover:bg-white disabled:cursor-wait disabled:opacity-60 sm:self-auto"
            >
              {state.isLoading ? 'Mencoba kembali…' : 'Coba lagi'}
            </button>
          </div>
        ) : state.isLoading ? (
          <p className="mb-5 text-sm text-lo-text-subtle" role="status">Memuat data keuangan…</p>
        ) : null}

        <div className="flex flex-col gap-6">
          {/* Row 1: Uang Bebas + Jatuh Tempo */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-stretch">
            <div className="lg:col-span-5">
              <UangBebasCard
                derived={state.derived}
                onOpenFormula={() => setFormulaOpen(true)}
              />
            </div>
            <div className={`${state.billsDue.length === 0 ? 'hidden md:block' : ''} lg:col-span-7`}>
              <JatuhTempoCard
                derived={state.derived}
                billsDue={state.billsDue}
                accounts={state.accounts}
                posItems={state.posItems}
                onPayBill={state.payBill}
                onPayAll={state.payAllBills}
                onPostponeBill={state.postponeBill}
              />
            </div>
          </div>

          {/* Row 2: Catat Transaksi */}
          <CatatTransaksiSection
            accounts={state.accounts}
            onSave={state.saveTransaction}
          />

          {/* Row 3: Yang Disisihkan + Agenda | Sumber Dana + Aktivitas Terkini */}
          <div className="grid min-w-0 grid-cols-1 lg:grid-cols-12 gap-4 sm:gap-6 items-start">
            <div className="order-2 min-w-0 lg:order-1 lg:col-span-7 flex flex-col gap-4 sm:gap-6">
              <YangDisisihkanSection
                posItems={state.posItems}
                accounts={state.accounts}
                freeCash={state.derived.freeCash}
                filter={state.posFilter}
                page={state.posPage}
                onFilterChange={state.setPosFilter}
                onPageChange={state.setPosPage}
                onTopUp={state.topUpPos}
                onWithdraw={state.withdrawPos}
                onUseIncremental={state.useIncrementalPos}
                onExecuteBatch={state.executeBatchPos}
                onExecuteSingle={state.executeSingleSpendPos}
                onCreate={state.createPos}
                onDelete={state.deletePos}
                onComplete={state.completePos}
                onUpdate={state.updatePos}
              />
              {/* Seukuran Yang Disisihkan, menutup ruang kosong di kolom kiri. */}
              <AgendaKasSection
                accounts={state.accounts}
                agendas={state.agendas}
                archivedAgendas={state.archivedAgendas}
                filter={state.agendaFilter}
                page={state.agendaPage}
                onFilterChange={state.setAgendaFilter}
                onPageChange={state.setAgendaPage}
                onSkip={state.skipAgenda}
                onScheduleFlexible={state.scheduleFlexibleAgenda}
                onStopCycle={state.cancelAgenda}
                onFinish={state.finishAgenda}
                onCreate={state.createAgenda}
                onUpdate={state.updateAgenda}
              />
            </div>
            <div className="order-1 min-w-0 lg:order-2 lg:col-span-5 flex flex-col gap-4 sm:gap-6">
              <SumberDanaSection
                accounts={state.accounts}
                onAdd={state.addAccount}
                onUpdate={state.updateAccount}
                onDelete={state.deleteAccount}
                onTransfer={state.transfer}
              />
              <AktivitasTerkiniSection
                transactions={state.transactions}
                accounts={state.accounts}
                filter={state.activityFilter}
                page={state.activityPage}
                onFilterChange={state.setActivityFilter}
                onPageChange={state.setActivityPage}
                onVoid={state.voidTransaction}
              />
            </div>
          </div>
        </div>
      </main>

      <AppFooter />
      <ToastViewport toasts={state.toasts} onDismiss={state.dismissToast} />
      <FormulaModal
        open={formulaOpen}
        onClose={() => setFormulaOpen(false)}
        derived={state.derived}
      />
    </div>
  );
}
