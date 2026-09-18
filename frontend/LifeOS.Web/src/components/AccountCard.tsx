import type { AccountProjection } from '../types';
import { accountTypeName } from '../types';

interface AccountCardProps {
  account: AccountProjection;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

export default function AccountCard({ account }: AccountCardProps) {
  return (
    <div className="account-card">
      <div className="account-card-header">
        <span className="account-name">{account.name}</span>
        <span className="account-type">{accountTypeName(account.type)}</span>
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
