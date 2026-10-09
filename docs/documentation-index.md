# Indeks dan Pemeliharaan Dokumentasi

Dokumen ini mencatat dokumentasi inti Life OS, sumber kebenaran untuk tiap topik, dan perubahan kode yang mengharuskan dokumentasi diperbarui. Tujuannya mencegah README, spesifikasi domain, dan prosedur test berbeda dari implementasi.

## Dokumentasi inti yang ada

| Dokumen | Menjadi sumber kebenaran untuk | Wajib diperbarui ketika |
|---|---|---|
| [`../README.md`](../README.md) | Ringkasan project, struktur repository, prasyarat, cara menjalankan aplikasi dan test | Perintah setup, dependency utama, environment variable, workflow test, atau lokasi dokumen berubah. Angka/count hasil test bersifat snapshot dan harus diperbarui bila tetap dicantumkan. |
| [`vision.md`](vision.md) | Tujuan produk, pengguna, ruang lingkup dan non-goals | Arah produk, target pengguna, tujuan utama, atau non-goals berubah. Tidak perlu diperbarui untuk perubahan implementasi biasa. |
| [`principles.md`](principles.md) | Prinsip lintas domain dan pedoman keputusan produk | Prinsip baru ditambahkan, diubah, atau dinyatakan tidak berlaku. |
| [`design.md`](design.md) | Arah visual, UX, interaksi, responsive behavior dan pola lintas modul | Pola UI/interaksi yang dipakai lintas layar berubah atau keputusan desain baru disepakati. |
| [`architecture.md`](architecture.md) | Scope, Owner/Guest, identitas, autentikasi dan batas arsitektur | Model scope, auth/session, isolasi data, komponen backend, persistence atau deployment boundary berubah. |
| [`finance.md`](finance.md) | Model domain finansial, ledger, invariant, istilah dan keputusan Finance v1 | Entitas, enum, semantik transaksi/saldo, reversal, cycle, endpoint domain atau keputusan produk finansial berubah. Perubahan aturan ledger harus memperbarui spesifikasi dan regression tests dalam PR yang sama. |
| [`api-reference.md`](api-reference.md) | Inventaris route, akses, konvensi wire/error dan tautan ke DTO | Route, metode, autentikasi, request/response, status error, enum atau pagination berubah. Detail command mengikuti DTO/OpenAPI. |
| [`operations-runbook.md`](operations-runbook.md) | Runtime configuration, migration, diagnosis health, release checklist dan batas prosedur produksi yang belum ditentukan | Environment variable/secret, port/CORS/cookie, migrations, health/monitoring, deployment atau lifecycle data berubah. Lengkapi detail yang ditandai pending sebelum bergantung pada prosedur produksi. |
| [`automated-testing-plan.md`](automated-testing-plan.md) | Lapisan test, cara menjalankan, CI, E2E, benchmark dan status gap | Test ditambah/dihapus, perintah berubah, dependency test berubah, workflow/branch check berubah, atau hasil verifikasi dan baseline diperbarui. |
| [`github-main-branch-ruleset.json`](github-main-branch-ruleset.json) | Template aturan GitHub untuk branch `main` | Nama branch target atau kebijakan pull request/force-push/deletion berubah. Jaga template ini selaras dengan ruleset aktif di GitHub. |

## Dokumentasi yang masih perlu dilengkapi

Dokumen berikut penting untuk mengurangi ketergantungan pada pengetahuan yang hanya ada di kode atau ingatan kontributor. Statusnya tidak dianggap lengkap hanya karena topiknya disentuh sebagian di dokumen lain.

| Prioritas | Dokumen/kesenjangan | Status dan cakupan berikutnya | Pemicu prioritas |
|---|---|---|---|
| P1 | **Detail kontrak API** | `api-reference.md` sudah mencatat seluruh route. Contoh lengkap JSON/schema per DTO dan contract-versioning belum didokumentasikan; OpenAPI saat ini tersedia di Development. | Lengkapi saat API punya konsumen eksternal/terpisah atau kontrak sering berubah. Perubahan route tetap harus memperbarui katalog dan HTTP/API tests. |
| P1 | **Prosedur operasi produksi** | `operations-runbook.md` menjelaskan konfigurasi yang diketahui. Provider deployment, backup/restore, langkah rollback dan alert thresholds belum ditetapkan; tandai sebagai pending. | Sebelum release/operasi produksi berikutnya. Jangan menyimpulkan backup atau rollback aman tanpa prosedur provider yang diuji. |
| P2 | **Keamanan, privasi dan lifecycle data** (`docs/security-and-data.md`) | Belum ada satu referensi operasional untuk data sensitif, secret handling, batas Owner/Guest, retensi/penghapusan, akses log, threat/abuse cases dan incident response. Beberapa prinsip ada di `principles.md` dan `architecture.md`. | Sebelum memperluas penyimpanan data pribadi, mengubah session/retensi, menambah integrasi pihak ketiga atau mengubah logging. |
| P2 | **Catatan keputusan arsitektur (ADR)** (`docs/decisions/`) | Belum ada decision log terpisah. Keputusan final saat ini tinggal di `architecture.md` dan `finance.md`; ADR tidak perlu menduplikasi semuanya. | Mulai saat keputusan lintas modul/infrastruktur sulit dibalik dan konteks, alternatif atau trade-off tidak cukup dijelaskan di spesifikasi. |

Prioritas menunjukkan urutan kebutuhan dokumentasi, bukan klaim bahwa fitur terkait sudah siap produksi. P1 runbook menjadi wajib sebelum prosedur produksi baru diandalkan.

## Aturan pembaruan

1. **Perbarui bersama perubahan.** Jika perubahan kode mengubah behavior atau cara menjalankan, dokumentasi terkait diperbarui dalam pull request yang sama.
2. **Satu topik punya satu sumber normatif.** README menjelaskan cara mulai dan menunjuk ke spesifikasi; README tidak menyalin seluruh aturan Finance atau Architecture.
3. **Pisahkan fakta dari rencana.** Gunakan label status seperti `Saat ini`, `Direncanakan`, `Belum ditentukan`, dan `Didefer` bila implementasi belum ada.
4. **Dokumentasikan command yang benar-benar dapat dijalankan.** Verifikasi command README setelah perubahan project/scripts/dependencies. Jangan mencantumkan perintah deployment tanpa target dan prosedur yang terkonfirmasi.
5. **Sinkronkan kontrak dan test.** Perubahan request/response harus memperbarui API reference, test HTTP/API dan mapping/frontend terkait.
6. **Jangan biarkan status lama menyesatkan.** Tinjau angka test, status workflow, nama required check, dan benchmark setiap kali CI/test pipeline berubah.
7. **Gunakan bahasa yang sesuai pembaca.** README dan runbook operasional berbahasa Indonesia; nama route, field, command dan istilah kode tetap mengikuti format teknis aslinya.

## Checklist dokumentasi untuk pull request

- Apakah setup, command, konfigurasi atau dependency berubah? Periksa README dan automated testing plan.
- Apakah user-visible behavior atau prinsip produk berubah? Periksa vision, principles dan design.
- Apakah model data, scope, invariant atau transaksi berubah? Periksa architecture dan finance.
- Apakah route atau format wire berubah? Perbarui API reference dan regression tests.
- Apakah deployment, secret, database migration, retensi atau operasi berubah? Periksa/tambahkan runbook dan security/data docs.
- Apakah workflow, status check atau ruleset berubah? Perbarui automated testing plan, README dan template ruleset.
