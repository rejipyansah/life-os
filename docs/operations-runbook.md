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
| `AI__Provider` | Opsional | Provider interpreter; default di kode adalah `Gemini`. Alternatif yang diimplementasikan adalah `Groq`. |
| `Gemini__ApiKey` | Saat `AI__Provider=Gemini` dan interpreter digunakan | Secret API Gemini. |
| `Gemini__Model` | Opsional | Model Gemini; default kode `gemini-3.5-flash-lite`. |
| `Groq__ApiKey` | Saat `AI__Provider=Groq` dan interpreter digunakan | Secret API Groq. |
| `Groq__Model` | Opsional | Model Groq; default kode `openai/gpt-oss-20b`. |
| `Groq__Endpoint` | Opsional | Endpoint Groq-compatible; memiliki default di kode. |
| `Groq__ReasoningEffort` | Opsional | Default kode `low`. |
| `ASPNETCORE_URLS` | Saat bind container/host secara eksplisit | Dockerfile mengatur `http://+:8080`. Pastikan HTTPS diterminasi di reverse proxy/load balancer produksi. |

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
- Log aplikasi menggunakan ASP.NET Core logging defaults dari `appsettings.json`; jangan menaruh password, token guest, isi transaksi atau data pribadi di log.
- Jika API tidak mulai: periksa log container, `ConnectionStrings__DefaultConnection`, reachability PostgreSQL, migrasi yang belum diterapkan, port/listener dan konfigurasi provider AI bila service interpret dipakai.
- Jika UI gagal berbicara dengan API: periksa `VITE_API_BASE_URL` untuk deployment frontend terpisah, konfigurasi proxy lokal, CORS origins pada `Program.cs`, cookie HTTPS/SameSite dan status endpoint API.

## Release dan rollback

Prosedur umum sebelum release:

1. Jalankan `npm test` dari root dengan PostgreSQL/E2E variables jika ingin memverifikasi seluruh lapisan. GitHub Actions saat ini dijeda karena billing lock.
2. Pastikan backup database tersedia dan restore procedure-nya diketahui.
3. Terapkan migration sebelum atau sesuai urutan release yang disepakati.
4. Deploy API dan frontend dengan environment configuration yang cocok.
5. Cek `/api/health`, guest/session flow, login owner dan alur finansial smoke terkontrol.
6. Pantau error API, database, serialization retry dan hasil E2E/benchmark.

Rollback aplikasi harus mengikuti kompatibilitas schema migration. **Belum ada prosedur rollback migration otomatis**; jangan menurunkan schema atau menghapus migration tanpa menilai dampak data/versi terlebih dahulu. Strategi backup/restore, deployment provider, rollback spesifik dan alert thresholds belum ditetapkan di repository dan perlu dilengkapi sebelum dipakai sebagai panduan produksi.

## Guest data dan retensi

Guest sandbox dibuat setelah user memilih demo/testing. Session yang kedaluwarsa tidak boleh diaktifkan kembali dari cookie lama; data guest ditahan sementara. Cleanup/deletion worker sengaja belum diimplementasikan menurut `architecture.md`. Jangan mengasumsikan data guest otomatis dihapus sampai kebijakan dan mekanisme cleanup ditetapkan.

## Perubahan yang mewajibkan runbook diperbarui

- Perubahan env var, port, CORS, cookie atau konfigurasi provider → update tabel konfigurasi/diagnosis.
- Perubahan migration/deployment/backup/restore/rollback → update prosedur release sebelum merge.
- Perubahan health endpoint, telemetry/logging atau alert → update diagnosis dan monitoring.
- Perubahan guest expiration, retensi atau cleanup → update bagian lifecycle dan `architecture.md`.
