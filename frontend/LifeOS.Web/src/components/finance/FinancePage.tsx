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
      />

      <main className="flex-1 w-full max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 pt-20 pb-16">
        <div className="flex flex-col gap-6">
          {/* Row 1: Uang Bebas + Jatuh Tempo */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-stretch">
            <div className="lg:col-span-5">
              <UangBebasCard
                derived={state.derived}
                onOpenFormula={() => setFormulaOpen(true)}
              />
            </div>
            <div className="lg:col-span-7">
              <JatuhTempoCard
                derived={state.derived}
                billsDue={state.billsDue}
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
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
            <div className="lg:col-span-7 flex flex-col gap-6">
              <YangDisisihkanSection
                posItems={state.posItems}
                accounts={state.accounts}
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
                onPostpone={state.postponeAgenda}
                onDelete={state.deleteAgenda}
                onFinish={state.finishAgenda}
                onCreate={state.createAgenda}
              />
            </div>
            <div className="lg:col-span-5 flex flex-col gap-6">
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
