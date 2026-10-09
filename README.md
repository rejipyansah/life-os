# Life OS

Life OS adalah aplikasi personal untuk mencatat dan memahami informasi penting dalam kehidupan sehari-hari. Repository ini berisi API ASP.NET Core, frontend React, dokumentasi produk, serta automated testing.

## Struktur proyek

- `backend/LifeOS.Api` — ASP.NET Core API dan akses PostgreSQL melalui Entity Framework Core.
- `backend/LifeOS.Tests` — unit/service tests cepat menggunakan SQLite in-memory.
- `backend/LifeOS.IntegrationTests` — test HTTP/API dan test provider PostgreSQL/Npgsql.
- `backend/FinanceProjectionBenchmark` — benchmark proyeksi keuangan dengan dataset sintetis PostgreSQL.
- `frontend/LifeOS.Web` — aplikasi React, Vitest tests, dan Playwright browser E2E.
- `docs` — visi, arsitektur, domain, desain, serta rencana automated testing.

Dokumen project utama:

- [Visi](docs/vision.md), [principles](docs/principles.md), dan [arah desain](docs/design.md).
- [Arsitektur](docs/architecture.md), [domain Finance](docs/finance.md), dan [referensi API](docs/api-reference.md).
- [Runbook operasi](docs/operations-runbook.md) dan [rencana automated testing](docs/automated-testing-plan.md).
- [Indeks dokumentasi dan aturan pembaruan](docs/documentation-index.md).

## Menjalankan aplikasi lokal

Prasyarat:

- .NET SDK 10
- Node.js 22 atau lebih baru
- PostgreSQL untuk menjalankan API

Jalankan API dengan connection string PostgreSQL pada konfigurasi `ConnectionStrings:DefaultConnection`, misalnya melalui environment variable:

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=lifeos;Username=lifeos;Password=<password>"
Push-Location backend
dotnet tool restore
dotnet tool run dotnet-ef -- database update --project LifeOS.Api/LifeOS.Api.csproj --startup-project LifeOS.Api/LifeOS.Api.csproj --configuration Release
Pop-Location
dotnet run --project backend/LifeOS.Api/LifeOS.Api.csproj
```

Di terminal lain, install dependency frontend dan jalankan Vite:

```powershell
npm ci --prefix frontend/LifeOS.Web
npm run dev --prefix frontend/LifeOS.Web
```

Frontend dev server meneruskan request `/api` ke API lokal di `http://localhost:5271`.

## Automated testing

Jalankan pemeriksaan lokal standar dari root repository:

```powershell
npm test
```

Perintah tersebut menjalankan:

- 385 backend unit/service tests.
- HTTP/API integration tests dengan TestServer dan SQLite in-memory.
- Frontend Vitest tests, lint, build tool benchmark, dan production build.

Test PostgreSQL-provider dan Playwright E2E otomatis ditambahkan bila connection string terkait tersedia. Satu file smoke test live frontend tetap skipped pada run normal karena memerlukan server/konfigurasi smoke tersendiri.

### Menjalankan semua test dengan PostgreSQL dan browser

1. Pastikan Docker Desktop berjalan, lalu mulai PostgreSQL test disposable:

```powershell
docker run --detach --rm --name lifeos-testing-postgres `
  --env POSTGRES_DB=lifeos_test `
  --env POSTGRES_USER=lifeos `
  --env POSTGRES_PASSWORD=lifeos_test_password `
  --publish 127.0.0.1:54328:5432 postgres:16
```

Tunggu sampai database siap:

```powershell
docker exec lifeos-testing-postgres pg_isready -U lifeos -d lifeos_test
```

2. Install dependency frontend dan browser Chromium Playwright satu kali:

```powershell
npm ci --prefix frontend/LifeOS.Web
npm exec --prefix frontend/LifeOS.Web -- playwright install chromium
```

3. Atur connection string ke database test tersebut dan jalankan semua pemeriksaan:

```powershell
$testConnection = "Host=127.0.0.1;Port=54328;Database=lifeos_test;Username=lifeos;Password=lifeos_test_password"
$env:LIFEOS_TEST_POSTGRES_CONNECTION = $testConnection
$env:LIFEOS_E2E_POSTGRES_CONNECTION = $testConnection
npm test
```

E2E menggunakan database PostgreSQL disposable, menerapkan migrasi aplikasi, menjalankan API dan frontend, lalu menguji perjalanan guest hingga pemasukan tercatat dan diproyeksikan. Script menolak E2E connection string jika nama database tidak mengandung `test` atau `e2e`.

Setelah selesai, hentikan container test:

```powershell
docker stop lifeos-testing-postgres
```

### Test E2E saja

Dengan PostgreSQL disposable dan Chromium tersedia:

```powershell
$env:LIFEOS_E2E_POSTGRES_CONNECTION = "Host=127.0.0.1;Port=54328;Database=lifeos_test;Username=lifeos;Password=lifeos_test_password"
npm run test:e2e
```

### Benchmark proyeksi keuangan

Benchmark menghasilkan set-aside dan entry ledger sintetis pada schema PostgreSQL sementara, mencatat waktu, jumlah query `SELECT`, serta alokasi memori, lalu menghapus schema tersebut.

```powershell
$env:LIFEOS_TEST_POSTGRES_CONNECTION = "Host=127.0.0.1;Port=54328;Database=lifeos_test;Username=lifeos;Password=lifeos_test_password"
dotnet run --project backend/FinanceProjectionBenchmark/FinanceProjectionBenchmark.csproj `
  --configuration Release -- --set-asides 100 --entries 50000 --iterations 5
```

Benchmark awal pada lingkungan development lokal menghasilkan rata-rata sekitar **92 ms**, **11 SELECT**, dan **4.14 MiB** alokasi untuk 100 set-aside dan 50.000 entry. Angka ini baseline pengamatan, bukan ambang CI; hasil dapat berbeda antar mesin. Workflow benchmark terjadwal/manual mengunggah hasil sebagai artifact.

## CI dan branch ruleset

GitHub Actions menjalankan `npm test` pada pull request dan push ke `main`/`master`. CI menggunakan PostgreSQL service dan Chromium Playwright. Ruleset untuk melindungi `main` tersedia di [`docs/github-main-branch-ruleset.json`](docs/github-main-branch-ruleset.json); check wajibnya adalah `Automated tests / test`.

Detail lebih lanjut mengenai lapisan test, status implementasi, dan gap selanjutnya ada di [`docs/automated-testing-plan.md`](docs/automated-testing-plan.md).
