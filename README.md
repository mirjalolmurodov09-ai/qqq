# AI Ustoz Pro

O'qituvchi uchun Windows dasturi: o'quv kalendari, dars jadvali, dars sanalarini avtomatik hisoblash, kalendar-tematik reja (KTR), davomat va hisobotlar. Barcha ma'lumotlar o'qituvchi kompyuterida (SQLite) saqlanadi, internet talab qilinmaydi.

**Joriy versiya: 0.1.0** — asosiy yadro. AI yordamchi, yuz orqali davomat va ovozli yordamchi keyingi versiyalarda (qarang: [docs/CHEKLOVLAR.md](docs/CHEKLOVLAR.md)).

## Yuklab olish

GitHub → **Actions** → oxirgi muvaffaqiyatli "Windows build va testlar" → **Artifacts** → `AiUstozPro-win-x64`:

- `AiUstozPro-Setup-0.1.0.exe` — o'rnatish dasturi (administrator huquqi shart emas);
- `AiUstozPro.exe` — portativ versiya (o'rnatmasdan ishga tushadi);
- `BUILD-REPORT.md` — versiya, sana, fayl hajmi, SHA256, test natijalari.

`v*` teg qo'yilganda (`git tag v0.1.0 && git push --tags`) shu fayllar GitHub **Releases** sahifasiga ham joylanadi.

## Arxitektura

```
src/
  AiUstozPro.Domain          — modellar va enumlar (bog'liqliksiz)
  AiUstozPro.Application     — sof biznes mantiq: sana generatori, qayta hisoblash, KTR joylashtirish,
                               import tekshiruvi, parol xeshlash, rollar
  AiUstozPro.Infrastructure  — EF Core + SQLite, servislar, audit, Excel/PDF/CSV eksport, zaxira nusxa
  AiUstozPro.App             — WPF (MVVM, CommunityToolkit.Mvvm), o'zbek tilidagi interfeys
tests/
  AiUstozPro.Tests           — xUnit: unit, integratsion va uchidan-uchigacha ssenariy
installer/                   — Inno Setup skripti
.github/workflows/build.yml  — Windows CI: test → build → single-file publish → smoke test → installer → o'rnatish/o'chirish sinovi
```

Texnologiyalar: .NET 10 (LTS), WPF, EF Core 10 (SQLite), CommunityToolkit.Mvvm 8.3, ClosedXML 0.104, QuestPDF 2024.12 (Community), xUnit.

## Mahalliy ishlab chiqish

Talab: Windows 10/11 x64, .NET 10 SDK, Visual Studio 2022 (17.14+) yoki Rider.

```powershell
dotnet test tests/AiUstozPro.Tests
dotnet run --project src/AiUstozPro.App
# Tarqatish uchun bitta .exe:
dotnet publish src/AiUstozPro.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
# Tayyor .exe ni ko'rinmas rejimda tekshirish:
publish\AiUstozPro.exe --smoke-test --data-dir %TEMP%\aup-smoke
```

`--data-dir` — ma'lumotlar papkasini o'zgartirish (standart: `%LOCALAPPDATA%\AiUstozPro`).

## Ma'lumotlar xavfsizligi

- Parollar PBKDF2-SHA256 (210 000 iteratsiya, tasodifiy tuz) bilan xeshlanadi; 5 marta noto'g'ri urinishdan keyin 15 daqiqaga qulflanadi.
- Har bir o'zgartirish amali rol bo'yicha tekshiriladi va audit jurnaliga yoziladi (davomat tuzatishlari — sabab bilan).
- Davomat: bir o'quvchiga bir darsda bitta yozuv — baza darajasida UNIQUE indeks.
- Qayta hisoblash davomat olingan, o'tilgan, tasdiqlangan, qo'lda qo'shilgan/ko'chirilgan darslarni hech qachon o'chirmaydi.
- Har kuni avtomatik zaxira (oxirgi 14 ta), sxema yangilanishidan oldin ham zaxira olinadi.

Batafsil: [docs/FOYDALANUVCHI-QOLLANMASI.md](docs/FOYDALANUVCHI-QOLLANMASI.md), [docs/ADMINISTRATOR-QOLLANMASI.md](docs/ADMINISTRATOR-QOLLANMASI.md).

© 2026 Murodov M.
