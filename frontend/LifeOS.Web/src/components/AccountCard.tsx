import type { AccountProjection } from '../types';

interface AccountCardProps {
  account: AccountProjection;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

function accountTypeLabel(type: string): string {
  switch (type) {
    case 'Cash': return 'Cash';
    case 'Bank': return 'Bank';
    case 'EWallet': return 'E-Wallet';
    default: return type;
  }
}

export default function AccountCard({ account }: AccountCardProps) {
  return (
    <div className="account-row">
      <div className="account-row-left">
        <span className="account-row-name">{account.name}</span>
        <span className="account-row-type">{accountTypeLabel(account.type)}</span>
      </div>
      <div className="account-row-right">
        <span className="account-row-balance">{formatCurrency(account.balance)}</span>
        <span className="account-row-available">avail. {formatCurrency(account.available)}</span>
      </div>
    </div>
  );
}
