# Ma'lum cheklovlar va keyingi bosqichlar (v0.1)

v0.1 — texnik topshiriqning **asosiy yadrosi**: kirish va rollar, guruh/o'quvchi/fan, o'quv kalendari, dars jadvali, sanalarni avtomatik hisoblash, KTR, qo'lda davomat, hisobotlar, zaxira. Quyidagilar ataylab keyingi bosqichlarga qoldirilgan va dasturda **ishlaydigandek ko'rsatilmagan**.

## Hali bajarilmagan modullar
| Modul | Holat | Izoh |
|---|---|---|
| AI yordamchi (TZ 11) | v0.2 | Sozlanadigan provayder: Claude API, OpenAI, lokal Ollama. Kalit Windows Credential Manager'da. AI natijalari o'qituvchi tasdiqlamaguncha rasmiy hujjatlarga tushmaydi. |
| Yuz orqali davomat (TZ 7) | v0.3 | Quyidagi huquqiy talablar hal qilingandan keyin. Natija har doim o'qituvchi tasdiqlaydigan "nomzod" bo'ladi. |
| Ovozli yordamchi (TZ 12) | v0.4 | Windows nutq API orqali; doimiy yozib olishsiz. |
| Test va baholash (TZ 14) | v0.3 | |
| Darsni boshqarish, lokal tarmoq (TZ 13) | Rejada | O'quvchi kompyuterlarida alohida agent kerak. |
| Word (.docx) eksport | v0.2 | Hozir Excel, PDF, CSV. |
| Rus/ingliz interfeysi, PostgreSQL | Rejada | |

## Texnik cheklovlar
- **Zaxira fayllari shifrlanmagan.** v0.2 da parol bilan AES-256 shifrlash rejalashtirilgan. Hozircha nusxalarni ishonchli joyda saqlang.
- **Ma'lumotlar bazasi fayli shifrlanmagan** (Windows foydalanuvchi profili huquqlari bilan himoyalangan). Umumiy kompyuterda har bir o'qituvchi alohida Windows hisobidan foydalanishi tavsiya etiladi.
- **Dastur raqamli imzolanmagan** — Windows SmartScreen ogohlantirishi mumkin. Kod imzolash sertifikati (Authenticode) olinsa, CI ga qo'shiladi.
- **Sxema migratsiyalari** EF Core migratsiya fayllari o'rniga versiyalangan SQL qadamlar bilan boshqariladi (`DatabaseMigrator`). Sababi: loyiha `dotnet ef` vositasini ishlatib bo'lmaydigan muhitda yozildi. Har bir yangilashdan oldin avtomatik zaxira olinadi.
- **Qorong'i rejim**: kiritish maydonlari (matn, ro'yxat, sana) o'qilishi uchun ataylab yorug' fonda qoladi.
- **Interfeysning qo'lda sinovi**: CI har bir sahifani ikkala rejimda ochib, XAML/bog'lanish xatolarini tekshiradi, lekin tugmalarni bosib ko'rmaydi. Haqiqiy foydalanuvchi sinovi (o'qituvchi tomonidan) talab qilinadi.
- **Bayramlar**: faqat sanasi qonunda qat'iy belgilangan bayramlar taklif qilinadi. Hayitlar va har yilgi qo'shimcha dam olish kunlari qo'lda kiritiladi.

## Yuz orqali davomat uchun huquqiy talablar (yurist bilan tekshirish uchun)
O'zbekiston Respublikasining "Shaxsga doir ma'lumotlar to'g'risida"gi Qonuni (2019-yil 2-iyul, O'RQ-547) biometrik ma'lumotlarni alohida himoya talab qiladigan toifaga kiritadi. Joriy etishdan oldin mahalliy yurist bilan quyidagilarni aniqlash kerak:

1. Voyaga yetmagan o'quvchilar biometrik ma'lumotlarini qayta ishlash uchun **ota-ona (qonuniy vakil) yozma roziligi** shakli va saqlash tartibi.
2. Litsey ma'lumotlar operatori sifatida **ro'yxatdan o'tishi** yoki xabardor qilish majburiyati bormi.
3. Shaxsga doir ma'lumotlarni **O'zbekiston hududidagi** texnik vositalarda saqlash talabi (lokalizatsiya) — dastur ma'lumotlarni faqat lokal kompyuterda saqlaydi, bulutga yubormaydi.
4. Ma'lumotlarni saqlash muddati, o'quvchi ketganda yoki rozilik qaytarib olinganda **o'chirish** tartibi.
5. Harbiy-akademik litsey maqomi bilan bog'liq qo'shimcha ichki talablar.
6. Muqobil (yuzsiz) davomat usuli doim mavjud bo'lishi — v0.1 dagi qo'lda davomat shu vazifani bajaradi.

Texnik yondashuv (rejada): xom rasmlar o'rniga shifrlangan yuz shablonlari (embedding), faqat lokal qayta ishlash, moslik chegarasi va har bir natijani o'qituvchi tasdiqlashi, liveness tekshiruvi kafolat sifatida ko'rsatilmaydi.
