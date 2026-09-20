import type { AccountProjection, AllocationProjection } from '../types';
import { updateAllocation } from '../api';

interface AllocationListProps {
  allocations: AllocationProjection[];
  accounts: AccountProjection[];
  onRefresh: () => void;
  onEdit: (allocation: AllocationProjection) => void;
}

function formatCurrency(amount: number): string {
  return `Rp ${amount.toLocaleString('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 })}`;
}

export default function AllocationList({ allocations, accounts, onRefresh, onEdit }: AllocationListProps) {
  const accountMap = new Map(accounts.map(a => [a.id, a.name]));
  const active = allocations.filter(a => a.isActive);
  const completed = allocations.filter(a => !a.isActive);

  const handleComplete = async (allocation: AllocationProjection) => {
    try {
      await updateAllocation(allocation.allocationId, { isActive: false });
      onRefresh();
    } catch {
      // silent
    }
  };

  if (allocations.length === 0) return null;

  return (
    <div className="allocation-section">
      <div className="allocation-head">
        <span className="section-title">Alokasi</span>
      </div>

      {active.length > 0 && (
        <div className="allocation-list">
          {active.map(a => (
            <div key={a.allocationId} className="allocation-item active">
              <div className="allocation-left">
                <span className="allocation-name">{a.name}</span>
                <span className="allocation-meta">
                  {formatCurrency(a.amount)}{accountMap.get(a.accountId) ? ` · ${accountMap.get(a.accountId)}` : ''}
                </span>
              </div>
              <div className="allocation-actions">
                <button className="allocation-action-btn" onClick={() => onEdit(a)}>
                  Edit
                </button>
                <button className="allocation-action-btn" onClick={() => handleComplete(a)}>
                  Selesai
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {completed.length > 0 && (
        <div className="allocation-history">
          <div className="allocation-history-label">Riwayat</div>
          {completed.map(a => (
            <div key={a.allocationId} className="allocation-item completed">
              <div className="allocation-left">
                <span className="allocation-name">{a.name}</span>
                <span className="allocation-meta">
                  {formatCurrency(a.amount)}{accountMap.get(a.accountId) ? ` · ${accountMap.get(a.accountId)}` : ''}
                </span>
              </div>
              <span className="allocation-status-badge">Selesai</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
