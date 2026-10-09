# Referensi API

Referensi ini merangkum HTTP surface Life OS yang saat ini tersedia. Aturan bisnis normatif tetap berada di [`finance.md`](finance.md); DTO dan implementasi endpoint tetap berada di `backend/LifeOS.Api`. Jika route, request/response, status atau autentikasi berubah, perbarui dokumen ini dan HTTP/API tests pada perubahan yang sama.

## Konvensi

- Base path API adalah `/api`.
- Endpoint Finance menyelesaikan Scope di server dari Identity cookie atau cookie guest `guest_session`. Client **tidak berwenang** menentukan `ScopeId`.
- Enum dikirim sebagai string JSON (contoh `"Income"`); nilai enum numerik tidak diterima.
- Response error buatan endpoint umumnya berbentuk `{ "error": "..." }`. Minimal API juga dapat mengembalikan `400 Bad Request` untuk JSON/body yang tidak bisa dideserialisasi.
- Operasi domain mengembalikan `422 Unprocessable Entity` untuk validasi, `404 Not Found` untuk resource yang tidak ditemukan pada scope tersebut, dan `409 Conflict` untuk konflik serialisasi yang tidak berhasil di-retry.
- Cookie guest dibuat/diperbarui lewat endpoint guest session. Request browser frontend memakai credentials/cookie.
- Di environment Development, OpenAPI tersedia di `/openapi/v1.json`. Definisi route dan DTO pada kode tetap sumber aktual; dokumen ini katalog navigasi, bukan pengganti OpenAPI.

## Health, auth dan guest

| Method | Path | Akses | Fungsi |
|---|---|---|---|
| `GET` | `/api/health` | Publik | Liveness sederhana; response `{ "status": "ok" }`. Tidak memeriksa koneksi database. |
| `GET` | `/api/auth/me` | Identity | Informasi identitas user yang sedang login. |
| `POST` | `/api/auth/login` | Publik | Login dengan body `email` dan `password`; berhasil mengeluarkan Identity cookie. |
| `POST` | `/api/auth/logout` | Identity | Mengakhiri sesi login. |
| `GET` | `/api/guest/session` | Guest | Memvalidasi/melanjutkan guest session dari cookie yang ada. Tidak membuat session baru. |
| `POST` | `/api/guest/session` | Guest | Membuat atau melanjutkan guest session; cookie `guest_session` dikembalikan server. |

Endpoint autentikasi lengkap memakai konfigurasi ASP.NET Core Identity di `Program.cs`.

## Finance state

| Method | Path | Fungsi |
|---|---|---|
| `GET` | `/api/finance/state` | Read model Finance terpadu untuk akun, alokasi, agenda, kewajiban, transaksi terbaru dan angka turunan. |

## Accounts

| Method | Path | Fungsi |
|---|---|---|
| `GET` | `/api/finance/accounts` | Daftar akun; query `includeArchived=true` menyertakan akun arsip. |
| `POST` | `/api/finance/accounts` | Membuat akun. |
| `GET` | `/api/finance/accounts/{id}` | Mengambil akun pada scope aktif. |
| `PATCH` | `/api/finance/accounts/{id}` | Memperbarui nama/tipe/status arsip sesuai aturan domain. |

## Transactions

| Method | Path | Fungsi |
|---|---|---|
| `GET` | `/api/finance/transactions` | Daftar transaksi pada scope aktif. |
| `GET` | `/api/finance/transactions/{id}` | Detail transaksi pada scope aktif. |
| `POST` | `/api/finance/transactions` | Membuat Income, Expense, Transfer, Refund atau transaksi domain lain yang didukung command. |
| `POST` | `/api/finance/transactions/{id}/reverse` | Membuat transaksi reversal append-only untuk transaksi asli. |

## Dana yang Disisihkan

| Method | Path | Fungsi |
|---|---|---|
| `GET` | `/api/finance/set-asides` | Daftar/proyeksi pos pada scope aktif. |
| `POST` | `/api/finance/set-asides` | Membuat pos. |
| `GET` | `/api/finance/set-asides/{id}` | Detail pos pada scope aktif. |
| `GET` | `/api/finance/set-asides/{id}/history` | Ledger pos; menerima query `cursor` dan `pageSize`. |
| `PATCH` | `/api/finance/set-asides/{id}` | Mengubah metadata pos. |
| `POST` | `/api/finance/set-asides/{id}/add` | Menambah alokasi. |
| `POST` | `/api/finance/set-asides/{id}/withdraw` | Mengurangi alokasi dan mengembalikan nilai ke uang belum dialokasikan. |
| `POST` | `/api/finance/set-asides/{id}/spend` | Mencatat expense nyata dari sumber dana dan mengoreksi ledger pos secara atomik. |
| `POST` | `/api/finance/set-asides/{id}/close` | Menutup pos. |

## Rencana Pengeluaran/Pemasukan

| Method | Path | Fungsi |
|---|---|---|
| `GET` | `/api/finance/upcoming-events` | Daftar agenda pada scope aktif. |
| `POST` | `/api/finance/upcoming-events` | Membuat agenda. |
| `GET` | `/api/finance/upcoming-events/{id}` | Detail agenda pada scope aktif. |
| `PATCH` | `/api/finance/upcoming-events/{id}` | Memperbarui agenda. |
| `POST` | `/api/finance/upcoming-events/{id}/postpone` | Menunda agenda. |
| `POST` | `/api/finance/upcoming-events/{id}/skip` | Menandai agenda dilewati. |
| `POST` | `/api/finance/upcoming-events/{id}/cancel` | Membatalkan agenda. |
| `POST` | `/api/finance/upcoming-events/{id}/realize` | Merealisasikan agenda menjadi transaksi; sumber dana dipilih saat realisasi. |
| `DELETE` | `/api/finance/upcoming-events/{id}` | Menghapus agenda yang masih dapat dihapus menurut lifecycle domain. |

## Interpretasi input natural language

| Method | Path | Fungsi |
|---|---|---|
| `POST` | `/api/interpret` | Menginterpretasi input user melalui provider AI yang dikonfigurasi dan memvalidasi hasil sebelum dikembalikan. Body memerlukan `input`. |

Endpoint ini memerlukan konfigurasi provider AI untuk dipakai. Finance API lainnya tidak memerlukan AI provider.

## Lokasi implementasi dan kontrak

- Auth, guest session, health dan interpret: `backend/LifeOS.Api/Program.cs`.
- Finance routes dan status/error handling: `backend/LifeOS.Api/Services/FinanceEndpoints.cs`.
- Request/response Finance: `backend/LifeOS.Api/Services/*Commands.cs` dan `FinanceStateProjection.cs`.
- Aturan Finance: [`finance.md`](finance.md).
- HTTP/API regression tests: `backend/LifeOS.IntegrationTests/FinanceEndpointTests.cs`.
