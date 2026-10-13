# Runbook Operasional

Dokumen ini mencatat konfigurasi/runtime yang diketahui dan prosedur aman yang sudah tersedia. Nilai spesifik deployment produksi, backup provider dan rollback database harus ditetapkan untuk environment yang benar sebelum digunakan; jangan menebak nilai tersebut dari development.

## Komponen

- API: ASP.NET Core .NET 10 (`backend/LifeOS.Api`).
- Database: PostgreSQL melalui Npgsql dan EF Core migrations.
- Frontend: React/Vite di `frontend/LifeOS.Web`; pada development `/api` diproxy ke `http://localhost:5271`.
- Container API: `Dockerfile`, image runtime ASP.NET 10, listen pada port `8080`, user container `1000:1000`.
- Test/benchmark tidak boleh menunjuk ke database produksi.

## Konfigurasi runtime API

ASP.NET Core dapat menerima key konfigurasi melalui environment variable dengan `__` sebagai pemisah hierarki.

| Key | Wajib kapan | Keterangan |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | API dan migrasi | Connection string PostgreSQL. Jangan commit credential. |
| `Groq__ApiKey` | API dijalankan | Secret API Groq. Groq adalah satu-satunya provider AI. |
| `Groq__Model` | Opsional | Model Groq; default kode `openai/gpt-oss-20b`, dipakai oleh request provider. |
| `Groq__Endpoint` | Opsional | Endpoint Groq-compatible; default `https://api.groq.com/openai/v1/`, dipakai sebagai base URL request. |
| `Groq__ReasoningEffort` | Opsional | Default kode `low`, dipakai oleh request provider. |
| `ASPNETCORE_URLS` | Saat bind container/host secara eksplisit | Dockerfile mengatur `http://+:8080`. Pastikan HTTPS diterminasi di reverse proxy/load balancer produksi. |

Origin frontend yang dipercaya untuk CORS dan request mutasi cookie didefinisikan di `backend/LifeOS.Api/Services/ApiSecurityPolicies.cs`. Production mengizinkan origin `https://life-os.rejipyansah.workers.dev`, `https://life-os.rejip.my.id`, dan `https://rejip.my.id`; localhost hanya diizinkan pada Development. Jika domain frontend berubah, perbarui daftar tersebut dan verifikasi request browser serta test CSRF. Mutasi API memerlukan header `Origin` atau `Referer` yang tepercaya.

Endpoint `POST /api/interpret` membatasi input hingga 2.000 karakter dan rate limit 10 request per menit per sesi guest, dengan IP sebagai fallback. Owner yang login dikecualikan dari rate limit aplikasi; kuota provider Groq tetap berlaku. Saat guest mencapai rate limit aplikasi atau kuota provider Groq habis, frontend dapat memakai parser lokal sebagai cadangan. Rate limiter memakai `RemoteIpAddress`; bila API berada di belakang reverse proxy, forwarded headers hanya boleh diaktifkan dengan daftar proxy/network tepercaya agar alamat client tidak dapat dipalsukan. Sampai konfigurasi proxy tepercaya ditetapkan, fallback IP dapat merepresentasikan alamat proxy bersama, bukan alamat client.

Nilai default kode berada di `backend/LifeOS.Api/appsettings.json`; file tersebut tidak boleh berisi secret produksi. `appsettings.Development.json` adalah konfigurasi lokal dan harus tetap menggunakan credential placeholder/non-produksi.

## Migrasi database

API tidak menerapkan migrations otomatis saat startup. Sebelum menjalankan versi API yang memerlukan schema baru:

1. Pastikan connection string menunjuk ke database environment yang benar.
2. Buat/konfirmasi backup yang dapat dipulihkan sesuai prosedur provider database environment tersebut.
3. Restore tool dan jalankan migration menggunakan proyek API:

```powershell
Push-Location backend
dotnet tool restore
dotnet tool run dotnet-ef -- database update --project LifeOS.Api/LifeOS.Api.csproj --startup-project LifeOS.Api/LifeOS.Api.csproj --configuration Release
Pop-Location
```

Jalankan prosedur ini hanya oleh operator yang berwenang. Database test/E2E wajib disposable; E2E runner memvalidasi bahwa nama database mengandung `test` atau `e2e`.

## Health check dan diagnosis awal

- `GET /api/health` mengembalikan `{ "status": "ok" }` ketika proses API merespons.
- Health endpoint saat ini **liveness sederhana**, bukan readiness check database. Response `200` tidak membuktikan PostgreSQL dapat menerima query.
- Log aplikasi menggunakan ASP.NET Core logging defaults dari `appsettings.json`; jangan menaruh password, token guest, isi transaksi atau data pribadi di log. Error API menyertakan `traceId`; gunakan nilai itu untuk mencocokkan respons client dengan log exception backend.
- Jika API tidak mulai: periksa log container, `ConnectionStrings__DefaultConnection`, reachability PostgreSQL, migrasi yang belum diterapkan, port/listener dan konfigurasi provider AI bila service interpret dipakai.
- Jika UI gagal berbicara dengan API: periksa `VITE_API_BASE_URL` untuk deployment frontend terpisah, konfigurasi proxy lokal, CORS/trusted origins pada `ApiSecurityPolicies.cs`, cookie HTTPS/SameSite dan status endpoint API.
- Jika mutasi API menerima `403`, periksa `Origin`/`Referer` browser dan sinkronisasi origin frontend pada `ApiSecurityPolicies.cs`; jika interpretasi menerima `429`, periksa frekuensi request per pengguna/sesi atau IP.
- Jika client melaporkan `traceId`, cari nilai tersebut di log API untuk menemukan detail exception server; respons `500` client sengaja tidak memuat stack trace atau pesan exception.

## Release dan rollback

Prosedur umum sebelum release:

1. Pastikan automated CI check `Automated tests / test` lulus.
2. Pastikan backup database tersedia dan restore procedure-nya diketahui.
3. Terapkan migration sebelum atau sesuai urutan release yang disepakati.
4. Deploy API dan frontend dengan environment configuration yang cocok.
5. Cek `/api/health`, guest/session flow, login owner dan alur finansial smoke terkontrol.
6. Pantau error API, database, serialization retry dan hasil E2E/benchmark.

Rollback aplikasi harus mengikuti kompatibilitas schema migration. **Belum ada prosedur rollback migration otomatis**; jangan menurunkan schema atau menghapus migration tanpa menilai dampak data/versi terlebih dahulu. Strategi backup/restore, deployment provider, rollback spesifik dan alert thresholds belum ditetapkan di repository dan perlu dilengkapi sebelum dipakai sebagai panduan produksi.

## Guest data dan retensi

Guest sandbox dibuat setelah user memilih demo/testing. Aktivitas valid pada API guest memperbarui `LastActivityAt` paling sering sekali per 15 menit. Session kedaluwarsa setelah 30 hari tidak aktif; cookie lama ditolak dan tidak dapat mengaktifkan scope kembali. Data sandbox yang kedaluwarsa ditahan 30 hari tambahan lalu dihapus oleh worker harian. Cleanup menggunakan PostgreSQL advisory lock agar hanya satu instance API menjalankan purge pada satu waktu. Worker mencatat kegagalan ke log dan mencoba lagi pada jadwal berikutnya.

Penghapusan mencakup data finansial turunan milik scope Guest dalam urutan yang mematuhi foreign key. Jangan menjalankan benchmark/test ke database produksi; sebelum mengubah urutan atau relasi cleanup, verifikasi dengan test SQLite dan PostgreSQL disposable. Jika cleanup gagal berulang, periksa error worker dan konektivitas database; tidak ada endpoint manual untuk menghapus Owner atau Guest scope.

## Perubahan yang mewajibkan runbook diperbarui

- Perubahan env var, port, CORS, cookie, trusted origins, rate limit atau konfigurasi provider → update tabel konfigurasi/diagnosis.
- Perubahan migration/deployment/backup/restore/rollback → update prosedur release sebelum merge.
- Perubahan health endpoint, telemetry/logging atau alert → update diagnosis dan monitoring.
- Perubahan guest expiration, retensi atau cleanup → update bagian lifecycle dan `architecture.md`.
