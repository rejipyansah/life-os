interface HomeHeaderProps {
  className?: string;
}

export default function HomeHeader({ className = '' }: HomeHeaderProps) {
  const now = new Date();
  const dayNames = ['Minggu', 'Senin', 'Selasa', 'Rabu', 'Kamis', 'Jumat', 'Sabtu'];
  const monthNames = ['Januari', 'Februari', 'Maret', 'April', 'Mei', 'Juni', 'Juli', 'Agustus', 'September', 'Oktober', 'November', 'Desember'];
  const day = dayNames[now.getDay()];
  const month = monthNames[now.getMonth()];
  const date = now.getDate();

  const hour = now.getHours();
  let greeting = 'Selamat pagi';
  if (hour >= 11 && hour < 15) greeting = 'Selamat siang';
  else if (hour >= 15 && hour < 18) greeting = 'Selamat sore';
  else if (hour >= 18) greeting = 'Selamat malam';

  return (
    <header className={`flex flex-col pb-6 md:pb-8 ${className}`}>
      <div className="flex items-center gap-2 mb-2">
        <span className="w-1.5 h-1.5 rounded-full bg-[#336946]" aria-hidden="true" />
        <span className="text-[11px] font-semibold uppercase tracking-widest text-lo-text-subtle">
          {day}, {date} {month} · Ritme Tenang
        </span>
      </div>
      <h1 className="font-headline text-[28px] md:text-[34px] lg:text-[40px] tracking-tight text-[#18241D] font-medium leading-tight mb-1">
        {greeting}, Reji.
      </h1>
      <p className="font-body text-sm md:text-base text-lo-text-subtle leading-relaxed">
        Ada beberapa hal yang berubah sejak terakhir kamu melihatnya.
      </p>
    </header>
  );
}
