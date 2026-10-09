# Ma'lum cheklovlar va keyingi bosqichlar (v0.4)

v0.2 — asosiy yadro (kirish va rollar, guruh/o'quvchi/fan, o'quv kalendari, dars jadvali, sanalarni avtomatik hisoblash, KTR, qo'lda davomat, hisobotlar, zaxira) + AI yordamchi, Word eksport, shifrlangan zaxira va test/baholash. Quyidagilar ataylab keyingi bosqichlarga qoldirilgan va dasturda **ishlaydigandek ko'rsatilmagan**.

## Hali bajarilmagan modullar
| Modul | Holat | Izoh |
|---|---|---|
| Yuz orqali davomat (TZ 7) | Huquqiy masala hal bo'lgach | Quyidagi huquqiy talablar hal qilingandan keyin. Natija har doim o'qituvchi tasdiqlaydigan "nomzod" bo'ladi. |
| Lokal tarmoq: o'quvchilarga topshiriq yuborish, ekranlarni ko'rish (TZ 13) | Rejada | O'quvchi kompyuterlarida alohida mijoz dasturi kerak; faqat aniq ruxsat va audit bilan. |
| Rus/ingliz interfeysi, PostgreSQL | Rejada | |

## Texnik cheklovlar
- **Avtomatik kunlik zaxiralar** foydalanuvchi profilida shifrsiz saqlanadi (Windows hisob huquqlari bilan himoyalangan). Fleshka yoki boshqa joyga faqat **shifrlangan nusxa** (Zaxira nusxalar → "Shifrlangan nusxa") olib chiqing. Parol unutilsa, shifrlangan nusxani tiklab bo'lmaydi.
- **AI yordamchi haqiqiy Claude / OpenAI / Ollama serverlarida sinalmagan**: CI muhitida internet va API kaliti yo'q, shuning uchun so'rov formati va javoblarni o'qish soxta server javoblari bilan tekshirilgan. Birinchi ishlatishda Sozlamalar → AI xizmati → **Ulanishni tekshirish** tugmasini bosing.
- **AI xarajati**: Claude va OpenAI pullik. Soatlik so'rovlar limiti (standart 30) xarajatni cheklaydi; narxlarni provayder saytidan tekshiring.
- **AI javoblari xato bo'lishi mumkin.** Dastur ularni "tekshirilmagan" deb belgilaydi; Word ga "o'qituvchi tekshirgan" belgisi bilan faqat tasdiqlangan matn chiqadi.
- AI javoblari oqim (streaming) bilan emas, to'liq tayyor bo'lgach ko'rsatiladi; uzun javob 30–90 soniya olishi mumkin.
- **Ma'lumotlar bazasi fayli shifrlanmagan** (Windows foydalanuvchi profili huquqlari bilan himoyalangan). Umumiy kompyuterda har bir o'qituvchi alohida Windows hisobidan foydalanishi tavsiya etiladi.
- **Dastur raqamli imzolanmagan** — Windows SmartScreen ogohlantirishi mumkin. Kod imzolash sertifikati (Authenticode) olinsa, CI ga qo'shiladi.
- **Sxema migratsiyalari** EF Core migratsiya fayllari o'rniga versiyalangan qadamlar bilan boshqariladi (`DatabaseMigrator`): yangi jadvallar EF modelidan yaratiladigan skriptdan olinadi, shuning uchun model bilan farq qilmaydi. v1 → v2 yangilanishi testda tekshirilgan. Har bir yangilashdan oldin avtomatik zaxira olinadi.
- **Qorong'i rejim**: kiritish maydonlari (matn, ro'yxat, sana) o'qilishi uchun ataylab yorug' fonda qoladi.
- **Interfeysning qo'lda sinovi**: CI har bir sahifani ikkala rejimda ochib, XAML/bog'lanish xatolarini tekshiradi, lekin tugmalarni bosib ko'rmaydi. Haqiqiy foydalanuvchi sinovi (o'qituvchi tomonidan) talab qilinadi.
- **Ovozli yordamchi — o'zbek tili**: o'zbekcha nutqni aniqlash faqat OpenAI orqali (internet va OpenAI kaliti, pullik). Windows'ning o'rnatilgan nutqni aniqlash moduli o'zbek tilini qo'llab-quvvatlamaydi (odatda faqat ingliz/rus). O'qib berish Windows'dagi ovozlar bilan — o'zbek ovozi odatda o'rnatilmagan, shuning uchun o'zbekcha matn rus yoki ingliz ovozida o'qiladi (talaffuz noaniq bo'ladi).
- Ovoz qurilmalari (mikrofon, karnay) CI muhitida yo'q — mikrofon va o'qib berish haqiqiy kompyuterda sinalishi kerak. Avtomatik testlar WAV kodlash, transkripsiya so'rovi va sozlamalarni tekshiradi.
- **Dars rejimidagi materiallar** fayl yo'li sifatida saqlanadi (nusxa olinmaydi) — fayl ko'chirilsa yoki o'chirilsa, havola ishlamaydi. Zaxira nusxaga materiallar fayllari kirmaydi.
- **Tezkor so'rov** sinfda qo'l ko'tarish asosida (o'qituvchi sanaydi) — shaxsiy natija yozilmaydi.
- **Test topshirish** hozircha 3 usulda: qog'ozda (javoblar o'qituvchi tomonidan kiritiladi), Excel/CSV import, yoki o'qituvchi kompyuterida navbatma-navbat. O'quvchilar kompyuterlarida lokal tarmoq orqali bir vaqtda topshirish keyingi bosqichda (alohida mijoz dasturi kerak).
- Testda faqat **bitta to'g'ri javobli** savollar qo'llab-quvvatlanadi.
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
