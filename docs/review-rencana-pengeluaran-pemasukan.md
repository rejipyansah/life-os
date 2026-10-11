# Rencana Pengeluaran/Pemasukan — aturan dan tindak lanjut

Dokumen ini merangkum keputusan UX/domain untuk fitur **Rencana Pengeluaran/Pemasukan** dan mencatat aturan Dana Bebas yang harus dijaga oleh implementasi.

## Aturan produk

- Modul ini menampung rencana pengeluaran terjadwal serta pemasukan dengan nominal dan waktu yang jelas. Pengeluaran harus terjadwal; pemasukan dapat terjadwal atau fleksibel.
- Label tanggal pada formulir adalah **Tanggal Mulai**, yaitu tanggal kejadian sekaligus jangkar recurrence. Untuk rencana berulang, setiap kejadian berikutnya mengikuti tanggal kejadian sebelumnya dan recurrence.
- Form menolak tanggal mulai sebelum hari ini dan membatasi nominal ke integer aman yang didukung aplikasi.
- Siklus awal form adalah **Satu Kali** agar rencana tidak berulang tanpa sengaja; backend dan input natural-language juga menolak tanggal terjadwal yang sudah lampau.
- Rencana bukan transaksi. Rencana tidak mengubah saldo rekening atau riwayat transaksi sampai pengguna menyelesaikannya sebagai kejadian nyata.
- Rencana pengeluaran berulang mengurangi **Dana Bebas** saat periode kalender yang memuat tanggal kejadian dimulai. Mingguan dimulai Senin, bulanan tanggal 1, kuartalan pada awal kuartal kalender, dan tahunan pada 1 Januari.
- Rencana Satu Kali langsung mengikat Dana Bebas saat dibuat, terlepas dari tanggal kejadian. Mengubah tanggal kejadian tidak mengubah waktu mulai komitmen; menghapus/menghentikan rencana melepasnya.
- Tanggal Jatuh Tempo tetap mengikuti tanggal kejadian sebenarnya; awal komitmen Dana Bebas dapat lebih awal. Perubahan tanggal pada rencana berulang dihitung ulang menurut cakupan periode baru. Jika dipindah ke periode berikutnya, komitmen dilepas sampai periode baru dimulai; jika masih dalam periode yang sama, komitmen tetap berlaku.
- **Lewati** melewati kejadian siklus sekarang dan membuat kejadian berikutnya; komitmen untuk kejadian yang dilewati dilepas.
- Perubahan tanggal rencana dilakukan melalui **Edit**; komitmen Dana Bebas dihitung ulang menurut siklus yang berlaku.
- **Hentikan Siklus** menghentikan rantai siklus dan menyimpan kejadian di arsip sebagai Dibatalkan; komitmen Dana Bebas yang aktif dilepas.
- Sumber Dana default wajib dipilih saat rencana dibuat untuk mempercepat pembayaran. Default tersebut hanya rekomendasi, dan masih dapat diganti ketika rencana direalisasikan.
- Penyelesaian rencana hanya meminta Sumber Dana aktual; pengalokasian ke Dana yang Disisihkan tetap dilakukan melalui alur transaksi/alokasi tersendiri.
- Rencana pemasukan fleksibel dapat dijadwalkan dari kartu melalui pemilihan tanggal baru.
- Rencana aktif dapat diedit tanpa menghapus dan membuat ulang.
- Kartu rencana menampilkan status terlambat/jatuh tempo hari ini; kategori tidak diulang pada nominal.
- Arsip membedakan pemasukan/pengeluaran melalui tanda nominal, menampilkan sumber dana default untuk kejadian dilewati/dibatalkan atau sumber dana aktual untuk kejadian selesai, dan menjelaskan statusnya.
- Pemasukan memakai kategori tetap **Pendapatan**. Pengeluaran harus memiliki tanggal serta kategori.

## UX yang perlu disediakan

- Tampilkan **Tanggal Mulai** pada formulir sebagai tanggal kejadian/jangkar siklus.
- Letakkan penjelasan perilaku siklus, Satu Kali, dan sumber dana di bawah Catatan agar form tetap ringkas.
- Izinkan perubahan rencana aktif tanpa membuat pengguna harus menghapus lalu membuat ulang.
- Tampilkan catatan dan Sumber Dana Default pada kartu rencana.
- Sediakan sumber dana default wajib pada pembuatan rencana dan preselect pada dialog realisasi, tetapi tetap izinkan penggantian.
- Jelaskan konsekuensi Lewati dan Hentikan Siklus pada konfirmasi/aksi.
- Validasi nominal positif dan dalam rentang yang didukung, serta tampilkan pesan kesalahan yang bisa ditindaklanjuti.

## Verifikasi teknis

- FreeCash dan ScheduledExpenseCommitments hanya menghitung UpcomingEvent berstatus Scheduled, Expense, Scheduled-kind, yang awal periode komitmennya <= tanggal bisnis WIB hari ini.
- DueObligations tetap menghitung kejadian yang sudah mencapai tanggalnya; angka ini bukan tambahan di luar komitmen FreeCash.
- Income, agenda fleksibel, agenda dengan periode mendatang, dan agenda nonaktif tidak mengurangi Dana Bebas.
- Perubahan tanggal, Lewati, Hentikan Siklus, realisasi, dan pembuatan kejadian lanjutan harus menghasilkan komitmen yang benar tanpa menulis transaksi untuk perubahan rencana.

## Status

- Perhitungan Dana Bebas berbasis awal periode kalender siklus: diterapkan.
- Tes regresi untuk tanggal masa depan, tanggal hari ini, dan penundaan: diterapkan/ditinjau bersama suite Finance.
- Validasi UI tanggal lampau dan batas nominal: diterapkan.
- Validasi tanggal lampau backend dan input natural-language: diterapkan.
- Siklus awal Satu Kali: diterapkan.
- Edit rencana, jadwal tanggal pemasukan fleksibel, badge status, dan arsip bertanda arah: diterapkan.
- Sumber Dana default wajib di form dan menjadi pilihan awal saat realisasi: diterapkan.
- Urutan field form dan keterangan ringkas di bawah Catatan: diterapkan.
- Label Tanggal Mulai dan tampilan catatan/default pada kartu: diterapkan.
- Edit rencana aktif dari daftar: diterapkan.
