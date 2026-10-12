# Verifikasi Performa Mobile

## Cara menjalankan

Pengujian membangun frontend production, lalu menyajikan aset production dengan kompresi gzip dan emulasi Pixel 7 pada Playwright Chromium. Mode API-backed menggunakan PostgreSQL disposable; mode mock melewati backend dan cocok untuk mengukur frontend saja. Kedua mode menggunakan Finance dengan 50 set-aside dan 100 agenda.

Prasyarat: Docker/PostgreSQL test disposable, .NET SDK 10, Node.js 22+, dependency frontend terpasang (`npm ci --prefix frontend/LifeOS.Web`), dan Chromium Playwright terpasang.

```powershell
$env:LIFEOS_E2E_POSTGRES_CONNECTION = "Host=127.0.0.1;Port=54328;Database=lifeos_test;Username=lifeos;Password=lifeos_test_password"
npm run test:mobile-perf
```

Database harus khusus test/E2E; script menolak nama database yang tidak mengandung `test` atau `e2e`. Secara default skenario dijalankan tiga kali. Jumlah pengulangan dapat diatur lewat `LIFEOS_MOBILE_PERF_REPEATS`. Port default API `5272` dan frontend `5174`; keduanya dapat diubah lewat `LIFEOS_E2E_API_PORT` dan `LIFEOS_E2E_FRONTEND_PORT`.

Frontend-only run tanpa PostgreSQL:

```powershell
$env:LIFEOS_MOBILE_PERF_MOCK = "1"
npm run test:mobile-perf
```

Profil jaringan menggunakan 150 ms RTT, 1,6 Mbps download, 750 Kbps upload, dan CPU slowdown 4x. Test mencatat LCP, CLS, durasi event browser sebagai indikator interaksi, waktu Finance siap, latency API, ukuran payload JSON, dan overflow horizontal.

## Hasil optimasi

Sebelum optimasi, Vite preview menyajikan aset tanpa kompresi: LCP median sekitar **3,96 detik**, Finance siap **4,15 detik**, dan transfer JS/CSS sekitar **489 KB**. Hasil itu belum memenuhi target LCP.

Optimasi berikut dilakukan:

- Logo yang ditampilkan pada ukuran 20–32 px diubah dari PNG 1280×1280 (627.143 byte) menjadi WebP 96×96 (2.060 byte), pengurangan sekitar **99,7%**.
- Ukuran intrinsic logo ditentukan agar browser dapat menata layout sebelum gambar selesai dimuat.
- Static server simulasi mengompres file teks/JS/CSS dengan gzip, mendekati kompresi delivery production.

Hasil API-backed sesudah optimasi, tiga pengulangan; angka median dan rentang:

| Metrik | Hasil | Acuan |
|---|---:|---:|
| LCP | **2,28 s** (2,25–2,33 s) | ≤ 2,5 s — tercapai |
| CLS | **0,064** | ≤ 0,1 — tercapai |
| Durasi event terlama browser | **32 ms** | Di bawah 200 ms; indikator sintetis, bukan INP lapangan |
| Finance siap | **2,47 s** (2,44–2,50 s) | Budget uji lokal ≤ 3 s — tercapai |
| Respons `GET /api/finance/state` | **173 ms** (172–173 ms) | Pengukuran lokal via Playwright |
| Payload Finance state | **98.366–98.378 byte** | JSON respons |
| JS + CSS + gambar yang ditransfer | **126.557 byte** | Aset production-style dengan gzip |
| Overflow horizontal | **0 px** | Tidak terdeteksi |

Setelah penambahan schema Zod runtime, frontend-only mock run terbaru menghasilkan LCP **2,23 s** (2,21–2,34 s), CLS **0,044**, Finance-ready **2,25 s** (2,19–2,27 s), dan transfer aset gzip **152.551 byte**. Request API dimock, jadi run ini hanya mengukur bundle, rendering, dan contract parsing.

**Kesimpulan:** mode API-backed sebelum perubahan Zod dan mode mock sesudahnya sama-sama memenuhi target LCP/CLS pada emulasi ini. Belum ada satu run terbaru yang menggabungkan schema Zod dengan PostgreSQL live. Ini juga bukan pengukuran ponsel fisik; deployment Cloudflare perlu diuji untuk memverifikasi Brotli, cache, CDN, dan INP lapangan.
