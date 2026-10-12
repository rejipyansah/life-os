namespace LifeOS.Api.Services;

public sealed record FinanceValidationError(string Code, string Message);

/// <summary>
/// Maps domain validation cases to stable wire codes and Indonesian user messages.
/// Domain exception text remains useful for internal tests/logging, but is never
/// sent to clients as-is.
/// </summary>
public static class FinanceValidationErrorCatalog
{
    private static readonly Dictionary<string, FinanceValidationError> ExactMessages =
        new(StringComparer.Ordinal)
        {
            ["Invalid request body."] = Item("invalid_request", "Isi permintaan tidak valid."),
            ["Input is required."] = Item("input_required", "Input wajib diisi."),
            ["Invalid email or password."] = Item("invalid_credentials", "Email atau kata sandi tidak valid."),
            ["Owner Scope not found."] = Item("owner_scope_not_found", "Ruang data pemilik tidak ditemukan."),
            ["Guest session not found."] = Item("NoGuestSession", "Sesi guest tidak ditemukan."),
            ["Request origin is not allowed."] = Item("forbidden", "Asal permintaan tidak diizinkan."),
            ["The request conflicted with a concurrent operation. Please try again."] = Item("concurrent_conflict", "Data berubah karena proses lain. Silakan coba lagi."),
            ["AI provider is temporarily unavailable. Please try again."] = Item("service_unavailable", "Layanan interpretasi sementara tidak tersedia. Silakan coba lagi."),
            ["Guest session expired or is no longer available."] = Item("GuestSessionExpired", "Sesi guest telah berakhir atau tidak lagi tersedia."),
            ["No existing guest session."] = Item("NoGuestSession", "Tidak ada sesi guest yang dapat dilanjutkan."),
            ["Guest session not found in current Scope."] = Item("NoGuestSession", "Sesi guest tidak ditemukan."),
            ["Transaction not found."] = Item("transaction_not_found", "Transaksi tidak ditemukan."),
            ["Set-aside not found."] = Item("set_aside_not_found", "Dana yang disisihkan tidak ditemukan."),
            ["Agenda not found."] = Item("agenda_not_found", "Agenda tidak ditemukan."),
            ["Account Name is required."] = Item("account_name_required", "Nama sumber dana wajib diisi."),
            ["Account Name cannot be empty."] = Item("account_name_required", "Nama sumber dana wajib diisi."),
            ["Account Name must not exceed 50 characters."] = Item("account_name_too_long", "Nama sumber dana maksimal 50 karakter."),
            ["Account Name must not exceed 256 characters."] = Item("account_name_too_long", "Nama sumber dana maksimal 256 karakter."),
            ["Scope not found."] = Item("scope_not_found", "Ruang data tidak ditemukan."),
            ["Account not found in current Scope."] = Item("account_not_found", "Sumber dana tidak ditemukan pada ruang data ini."),
            ["Transaction not found in current Scope."] = Item("transaction_not_found", "Transaksi tidak ditemukan pada ruang data ini."),
            ["Related transaction not found in current Scope."] = Item("related_transaction_not_found", "Transaksi terkait tidak ditemukan pada ruang data ini."),
            ["Set-aside not found in current Scope."] = Item("set_aside_not_found", "Dana yang disisihkan tidak ditemukan."),
            ["Set-aside not found or not active in current Scope."] = Item("set_aside_not_active", "Dana yang disisihkan tidak ditemukan atau sudah ditutup."),
            ["Jenis dana tidak valid."] = Item("set_aside_kind_invalid", "Jenis dana tidak valid."),
            ["Jenis siklus tidak valid."] = Item("cycle_kind_invalid", "Jenis siklus tidak valid."),
            ["Rutinitas berkala harus memiliki periode berulang."] = Item("recurrence_required", "Rutinitas berkala harus memiliki periode berulang."),
            ["Rutinitas berkala ini sudah direalisasikan pada periode berjalan."] = Item("cycle_already_executed", "Rutinitas berkala sudah direalisasikan pada periode ini."),
            ["Closed set-aside cannot be updated."] = Item("set_aside_closed", "Dana yang sudah ditutup tidak dapat diperbarui."),
            ["Closed set-aside cannot be changed."] = Item("set_aside_closed", "Dana yang sudah ditutup tidak dapat diubah."),
            ["Closed set-aside cannot be spent."] = Item("set_aside_closed", "Dana yang sudah ditutup tidak dapat digunakan."),
            ["Set-aside is already closed."] = Item("set_aside_closed", "Dana yang disisihkan sudah ditutup."),
            ["Set-aside amount must not be negative."] = Item("set_aside_amount_invalid", "Saldo awal dana tidak boleh negatif."),
            ["Add amount must be positive."] = Item("amount_must_be_positive", "Nominal penambahan harus lebih besar dari nol."),
            ["Withdrawal amount must be positive."] = Item("amount_must_be_positive", "Nominal penarikan harus lebih besar dari nol."),
            ["Spend amount must be positive."] = Item("amount_must_be_positive", "Nominal pemakaian harus lebih besar dari nol."),
            ["A cycling set-aside requires a target amount."] = Item("cycle_target_required", "Dana dengan siklus harus memiliki nominal target."),
            ["Set-aside name is required."] = Item("set_aside_name_required", "Nama dana yang disisihkan wajib diisi."),
            ["Set-aside name must not exceed 256 characters."] = Item("set_aside_name_too_long", "Nama dana yang disisihkan maksimal 256 karakter."),
            ["Target amount must not be negative."] = Item("target_amount_invalid", "Nominal target tidak boleh negatif."),
            ["Alasan penutupan tidak valid."] = Item("close_reason_invalid", "Alasan penutupan tidak valid."),
            ["Cursor riwayat tidak valid."] = Item("history_cursor_invalid", "Penanda halaman riwayat tidak valid."),
            ["Income Amount must be positive."] = Item("income_amount_invalid", "Nominal pemasukan harus lebih besar dari nol."),
            ["Income must have exactly one entry."] = Item("income_entries_invalid", "Pemasukan harus memiliki satu rincian sumber dana."),
            ["Income entry Amount must be positive."] = Item("income_entry_invalid", "Rincian pemasukan harus bernilai positif."),
            ["Expense Amount must be positive."] = Item("expense_amount_invalid", "Nominal pengeluaran harus lebih besar dari nol."),
            ["Expense must have exactly one entry."] = Item("expense_entries_invalid", "Pengeluaran harus memiliki satu rincian sumber dana."),
            ["Expense entry Amount must be negative."] = Item("expense_entry_invalid", "Rincian pengeluaran harus bernilai negatif."),
            ["Expense entry Amount must equal the negative Transaction Amount."] = Item("expense_entry_mismatch", "Nominal rincian tidak sesuai dengan nominal pengeluaran."),
            ["Transfer Amount must be positive."] = Item("transfer_amount_invalid", "Nominal transfer harus lebih besar dari nol."),
            ["Transfer must have exactly two entries (source and destination)."] = Item("transfer_entries_invalid", "Transfer harus memiliki sumber dan tujuan dana."),
            ["Transfer source and destination must differ."] = Item("transfer_accounts_must_differ", "Sumber dan tujuan transfer harus berbeda."),
            ["Transfer source entry Amount must be negative."] = Item("transfer_source_invalid", "Rincian sumber transfer harus bernilai negatif."),
            ["Transfer destination entry Amount must be positive."] = Item("transfer_destination_invalid", "Rincian tujuan transfer harus bernilai positif."),
            ["FeeAmount must not be negative."] = Item("transfer_fee_invalid", "Biaya transfer tidak boleh negatif."),
            ["FeeAmount is not applicable for Income."] = Item("fee_not_applicable", "Biaya tidak berlaku untuk pemasukan."),
            ["FeeAmount is not applicable for Expense."] = Item("fee_not_applicable", "Biaya tidak berlaku untuk pengeluaran."),
            ["FeeAmount is not applicable for Refund."] = Item("fee_not_applicable", "Biaya tidak berlaku untuk pengembalian dana."),
            ["FeeAmount is not applicable for Reversal."] = Item("fee_not_applicable", "Biaya tidak berlaku untuk pembatalan transaksi."),
            ["FeeAmount is not applicable for Adjustment."] = Item("fee_not_applicable", "Biaya tidak berlaku untuk penyesuaian saldo."),
            ["Insufficient balance."] = Item("insufficient_balance", "Saldo sumber dana tidak mencukupi."),
            ["Insufficient balance in source Account."] = Item("insufficient_balance", "Saldo sumber dana tidak mencukupi."),
            ["Refund requires RelatedTransactionId."] = Item("refund_reference_required", "Pengembalian dana harus merujuk transaksi asal."),
            ["Refund can only reference an Expense transaction."] = Item("refund_reference_invalid", "Pengembalian dana hanya dapat merujuk pengeluaran."),
            ["Refund Amount must be positive."] = Item("refund_amount_invalid", "Nominal pengembalian dana harus lebih besar dari nol."),
            ["Refund must have exactly one entry."] = Item("refund_entries_invalid", "Pengembalian dana harus memiliki satu rincian sumber dana."),
            ["Refund entry Amount must be positive."] = Item("refund_entry_invalid", "Rincian pengembalian dana harus bernilai positif."),
            ["Reversal requires RelatedTransactionId."] = Item("reversal_reference_required", "Pembatalan harus merujuk transaksi asal."),
            ["This transaction has already been reversed."] = Item("transaction_already_reversed", "Transaksi ini sudah dibatalkan."),
            ["Reversal must have at least one entry."] = Item("reversal_entries_invalid", "Pembatalan harus memiliki rincian transaksi."),
            ["Reversal entry Amount must be non-zero."] = Item("reversal_entry_invalid", "Rincian pembatalan tidak boleh bernilai nol."),
            ["Adjustment Amount must be positive."] = Item("adjustment_amount_invalid", "Nominal penyesuaian harus lebih besar dari nol."),
            ["Adjustment must have exactly one entry."] = Item("adjustment_entries_invalid", "Penyesuaian harus memiliki satu rincian sumber dana."),
            ["Adjustment entry Amount must be non-zero."] = Item("adjustment_entry_invalid", "Rincian penyesuaian tidak boleh bernilai nol."),
            ["Transaction has no entries to reverse."] = Item("transaction_entries_missing", "Transaksi tidak memiliki rincian yang dapat dibatalkan."),
            ["Reversal reason must not exceed 512 characters."] = Item("reversal_reason_too_long", "Alasan pembatalan maksimal 512 karakter."),
            ["Agenda amount must be positive."] = Item("agenda_amount_invalid", "Nominal agenda harus lebih besar dari nol."),
            ["Only a scheduled agenda can be updated."] = Item("agenda_status_invalid", "Hanya agenda terjadwal yang dapat diperbarui."),
            ["Only a scheduled agenda can be postponed."] = Item("agenda_status_invalid", "Hanya agenda terjadwal yang dapat ditunda."),
            ["A flexible agenda has no date to postpone. Provide a new due date."] = Item("agenda_date_required", "Agenda fleksibel memerlukan tanggal baru untuk dijadwalkan."),
            ["A flexible agenda must be postponed to today or a future date."] = Item("agenda_date_in_past", "Agenda fleksibel hanya dapat dijadwalkan hari ini atau setelahnya."),
            ["Postponing must move the due date forward."] = Item("agenda_date_must_move_forward", "Tanggal penundaan harus lebih akhir dari tanggal sebelumnya."),
            ["Only a scheduled agenda can be settled."] = Item("agenda_status_invalid", "Hanya agenda terjadwal yang dapat dilewati atau dibatalkan."),
            ["A realized agenda cannot be deleted; reverse its transaction instead."] = Item("realized_agenda_cannot_delete", "Agenda terealisasi tidak dapat dihapus; batalkan transaksinya."),
            ["Only a scheduled agenda can be realized."] = Item("agenda_status_invalid", "Hanya agenda terjadwal yang dapat direalisasikan."),
            ["This realization must specify a source account (Sumber Dana). Select the actual account where money leaves/enters."] = Item("source_account_required", "Pilih sumber dana aktual untuk merealisasikan agenda."),
            ["A scheduled agenda requires a due date."] = Item("agenda_due_date_required", "Agenda terjadwal harus memiliki tanggal jatuh tempo."),
            ["A scheduled agenda date cannot be before today."] = Item("agenda_date_in_past", "Tanggal agenda tidak boleh sebelum hari ini."),
            ["A recurring agenda must have a scheduled due date."] = Item("recurring_agenda_requires_date", "Agenda berulang harus memiliki jadwal dan tanggal jatuh tempo."),
            ["Agenda not found in current Scope."] = Item("agenda_not_found", "Agenda tidak ditemukan pada ruang data ini."),
        };

    public static FinanceValidationError Resolve(string? sourceMessage, int statusCode = 422)
    {
        if (string.IsNullOrWhiteSpace(sourceMessage))
            return Generic(statusCode);

        var message = sourceMessage.Trim();
        if (ExactMessages.TryGetValue(message, out var exact))
            return exact;

        if (message.StartsWith("Akun dengan nama '", StringComparison.Ordinal))
            return Item("duplicate_account_name", "Nama sumber dana tersebut sudah digunakan pada ruang data ini.");
        if (message.StartsWith("Akun belum bisa diarsipkan", StringComparison.Ordinal))
            return Item("account_balance_not_zero", "Sumber dana tidak dapat diarsipkan karena saldonya belum nol.");
        if (message.StartsWith("Invalid AccountType:", StringComparison.Ordinal))
            return Item("account_type_invalid", "Jenis sumber dana tidak valid.");
        if (message.StartsWith("Unsupported TransactionType:", StringComparison.Ordinal))
            return Item("transaction_type_invalid", "Jenis transaksi tidak didukung.");
        if (message.StartsWith("Account(s) not found in current Scope:", StringComparison.Ordinal))
            return Item("account_not_found", "Satu atau beberapa sumber dana tidak ditemukan pada ruang data ini.");
        if (message.StartsWith("Cannot use archived Account", StringComparison.Ordinal))
            return Item("account_archived", "Sumber dana telah diarsipkan dan tidak dapat digunakan.");
        if (message.StartsWith("Uang Bebas tidak cukup", StringComparison.Ordinal))
            return Item("free_cash_insufficient", "Uang Bebas tidak mencukupi untuk tindakan ini.");
        if (message.StartsWith("Insufficient available money", StringComparison.Ordinal))
            return Item("free_cash_insufficient", "Uang Bebas tidak mencukupi untuk tindakan ini.");
        if (message.StartsWith("Insufficient balance in '", StringComparison.Ordinal))
            return Item("insufficient_balance", "Saldo sumber dana tidak mencukupi.");
        if (message.StartsWith("Insufficient set-aside balance", StringComparison.Ordinal))
            return Item("set_aside_balance_insufficient", "Saldo dana yang disisihkan tidak mencukupi.");
        if (message.StartsWith("Transfer source Amount must be", StringComparison.Ordinal))
            return Item("transfer_source_mismatch", "Rincian sumber transfer tidak sesuai dengan nominal dan biaya.");
        if (message.StartsWith("Transfer destination Amount must be", StringComparison.Ordinal))
            return Item("transfer_destination_mismatch", "Rincian tujuan transfer tidak sesuai dengan nominal.");
        if (message.StartsWith("Transaction Amount must not exceed", StringComparison.Ordinal)
            || message.StartsWith("Transaction entry Amount must not exceed", StringComparison.Ordinal))
            return Item("transaction_amount_too_large", "Nominal transaksi melebihi batas yang diizinkan.");
        if (message.StartsWith("FeeAmount must not exceed", StringComparison.Ordinal))
            return Item("fee_amount_too_large", "Nominal biaya melebihi batas yang diizinkan.");
        if (message.StartsWith("Description must not exceed", StringComparison.Ordinal))
            return Item("description_too_long", "Deskripsi transaksi terlalu panjang.");
        if (message.StartsWith("CategoryName must not exceed", StringComparison.Ordinal))
            return Item("category_too_long", "Nama kategori terlalu panjang.");
        if (message.StartsWith("Agenda amount cannot exceed", StringComparison.Ordinal))
            return Item("agenda_amount_too_large", "Nominal agenda melebihi batas yang diizinkan.");
        if (message.StartsWith("Agenda title is required.", StringComparison.Ordinal))
            return Item("agenda_title_required", "Judul agenda wajib diisi.");
        if (message.StartsWith("Invalid direction:", StringComparison.Ordinal))
            return Item("agenda_direction_invalid", "Jenis arus kas agenda tidak valid.");
        if (message.StartsWith("Invalid schedule kind:", StringComparison.Ordinal))
            return Item("agenda_schedule_invalid", "Jenis jadwal agenda tidak valid.");
        if (message.StartsWith("Invalid recurrence:", StringComparison.Ordinal))
            return Item("agenda_recurrence_invalid", "Pola pengulangan agenda tidak valid.");
        if (message.StartsWith("Invalid date:", StringComparison.Ordinal))
            return Item("date_invalid", "Format tanggal tidak valid.");

        return Generic(statusCode);
    }

    private static FinanceValidationError Generic(int statusCode) => statusCode switch
    {
        400 => Item("invalid_request", "Permintaan tidak valid."),
        401 => Item("unauthorized", "Sesi tidak valid. Silakan masuk kembali."),
        403 => Item("forbidden", "Akses ditolak."),
        404 => Item("not_found", "Data yang diminta tidak ditemukan."),
        409 => Item("concurrent_conflict", "Data berubah karena proses lain. Silakan coba lagi."),
        413 => Item("payload_too_large", "Data permintaan terlalu besar."),
        422 => Item("validation_failed", "Data Finance tidak valid. Periksa kembali isian."),
        429 => Item("rate_limited", "Terlalu banyak permintaan. Silakan coba lagi nanti."),
        503 => Item("service_unavailable", "Layanan sementara tidak tersedia. Silakan coba lagi nanti."),
        _ => Item("request_failed", "Permintaan tidak dapat diproses. Silakan coba lagi.")
    };

    private static FinanceValidationError Item(string code, string message) => new(code, message);
}
