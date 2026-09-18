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
    <div className="account-card">
      <div className="account-card-header">
        <span className="account-name">{account.name}</span>
        <span className="account-type">{accountTypeLabel(account.type)}</span>
      </div>
      <div className="account-card-body">
        <div className="account-balance">
          <span className="label">Balance</span>
          <span className="value">{formatCurrency(account.balance)}</span>
        </div>
        <div className="account-available">
          <span className="label">Available</span>
          <span className="value">{formatCurrency(account.available)}</span>
        </div>
      </div>
    </div>
  );
}
