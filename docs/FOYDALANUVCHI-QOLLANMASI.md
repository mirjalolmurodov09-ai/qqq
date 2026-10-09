# AI Ustoz Pro — foydalanuvchi qo'llanmasi (v0.4)

## 1. O'rnatish va birinchi ishga tushirish

1. `AiUstozPro-Setup-0.4.0.exe` ni ishga tushiring. Administrator huquqi so'ralmaydi; dastur `%LOCALAPPDATA%\Programs\AI Ustoz Pro` ga o'rnatiladi. Windows SmartScreen "Noma'lum nashriyotchi" deb ogohlantirsa — **Batafsil → Baribir ishga tushirish** (dastur raqamli imzolanmagan, qarang: CHEKLOVLAR).
2. Birinchi ishga tushirishda **administrator hisobini** yarating: F.I.Sh., login (lotin harflari) va parol (kamida 8 belgi, harf va raqam).
3. Bosh sahifadagi **"Ishni boshlash uchun"** ro'yxati qadamlarni ko'rsatadi.

## 2. Ish tartibi (tavsiya etilgan ketma-ketlik)

### 2.1. O'quv kalendari
- O'quv yili nomi, boshlanish va tugash sanalarini kiriting, **dars kunlarini** belgilang (masalan, 6 kunlik hafta uchun Du–Sha). **Saqlash**.
- Dastur sanasi qonun bilan belgilangan bayramlarni qo'shishni taklif qiladi (1-yanvar, 8-mart, 21-mart, 9-may, 1-sentabr, 1-oktabr, 8-dekabr).
- **Ramazon va Qurbon hayiti, qo'shimcha dam olish kunlari, ko'chirilgan ish kunlari** har yili rasmiy qaror bilan e'lon qilinadi — ularni o'zingiz kiriting. Dastur bu sanalarni taxmin qilmaydi.
- **Ta'til** — boshlanish va tugash sanasi bilan. **Ko'chirilgan ish kuni** — dam olish kunida qaysi hafta kuni jadvali bo'yicha dars o'tilishini tanlang.
- Istisnoni faqat bitta guruh yoki fanga tegishli qilish mumkin (masalan, guruh ekskursiyada).
- Semestr/chorak chegaralarini ham shu yerda kiritasiz.

### 2.2. Guruhlar, o'quvchilar, fanlar
- Guruh yarating, o'quvchilarni qo'lda kiriting yoki **Import (Excel/CSV)** orqali yuklang. Namuna: `samples/oquvchilar-namuna.csv`.
- Import oynasi ustunlarni avtomatik moslaydi, siz o'zgartirishingiz mumkin. Har bir satr oldindan tekshiriladi: xatolilar import qilinmaydi.
- O'quvchi raqami tizimda noyob. Bir xil F.I.Sh. li o'quvchilar **birlashtirilmaydi** — dastur faqat ogohlantiradi.
- O'quvchi o'chirilmaydi, **arxivlanadi** — davomat tarixi saqlanadi.

### 2.3. Dars jadvali
- Har bir dars uchun: guruh, fan, o'qituvchi, hafta kuni, dars raqami, vaqt, xona, akademik soat (odatda 2). Ixtiyoriy — amal qilish davri (masalan faqat 1-semestr).
- Bir vaqtda o'qituvchi, xona yoki guruh band bo'lsa — dastur ogohlantiradi; xohlasangiz baribir saqlaysiz.
- **Haftalik ko'rinish** jadvalni hafta bo'yicha ko'rsatadi. Excel/CSV dan import qilish mumkin (`samples/dars-jadvali-namuna.csv`).

### 2.4. Dars sanalari (avtomatik hisoblash)
1. Guruh va fanni tanlang → **Hisoblash (ko'rib chiqish)**.
2. O'ng tomonda natija: qaysi sanalar qo'shiladi, qaysi kunlar bayram/ta'til sababli o'tilmaydi (sababi bilan), qaysilari o'chiriladi va **ziddiyatlar**.
3. **Tasdiqlash va saqlash**.

Kalendar yoki jadval o'zgarsa, qayta hisoblang. Davomat olingan, o'tilgan, tasdiqlangan, qo'shimcha va ko'chirilgan darslar **hech qachon avtomatik o'chirilmaydi** — ular ziddiyat sifatida ko'rsatiladi, siz hal qilasiz.

Qo'lda tahrirlash: **Bekor qilish** (sabab bilan), **Tiklash**, **Boshqa kunga ko'chirish**, **Qo'shimcha dars**, **O'tildi / o'tilmadi**, **O'chirish**. Oylar bo'yicha filtr bor.

### 2.5. Kalendar-tematik reja (KTR)
1. Guruh va fanni tanlang → **KTR yaratish**.
2. **Import (Excel/CSV)**: majburiy ustunlar — Mavzu nomi, Soat. Namuna: `samples/ktr-namuna.csv`.
3. Joylashtirish qoidasini tanlang: *soatlar darslarga bo'linadi* (4 soatlik mavzu = ikkita 2 soatlik dars) yoki *har bir mavzu bitta darsga*.
4. **Ko'rib chiqish** — qaysi mavzu sanasi qanday o'zgarishi (avvalgi va yangi reja farqi), darslar yetmasa ogohlantirish. **Qo'llash** — saqlash.
5. Mavzu o'tilgach **O'tildi** — mavzu qulflanadi va qayta joylashtirishda o'z darsida qoladi.
6. Mavzu tugallanmasa — **Keyingi darsga davom**: mavzuga soat qo'shiladi va keyingi mavzular suriladi (farq oldin ko'rsatiladi).
7. Dars bekor qilinsa — KTR ni qayta joylashtiring, keyingi mavzular yangi haqiqiy sanalarga suriladi.

Ranglar: yashil — o'tilgan, sariq — rejadan ortda, qizil — dars sanasi yetmagan.

### 2.6. Davomat
- Sanani tanlang (bosh sahifadagi **Davomat olish** tugmasi bugungi darsni to'g'ridan-to'g'ri ochadi).
- Har bir o'quvchi uchun holat: Keldi, Kechikdi, Sababli kelmadi, Sababsiz kelmadi, Ruxsat bilan chiqdi, Aniqlanmadi (tekshirish kerak). **Belgilanmaganlarni "Keldi"** — tezkor tugma.
- **Saqlash**. Avval saqlangan yozuvni o'zgartirsangiz, dastur **tuzatish sababini** so'raydi va audit jurnaliga yozadi.
- Kelajakdagi yoki bekor qilingan darsga davomat olinmaydi. O'qituvchi faqat o'z darsida davomat oladi.

### 2.7. Hisobotlar
- **Davomat jurnali** — o'quvchilar × sanalar, jami va foiz; kunlik/haftalik/oylik/yillik davr tugmalari.
- **KTR: reja va amaliyot** — oylar kesimi, o'tilgan/qolgan soatlar, rejadan ortda qolish.
- **Darslar statistikasi**, **Audit jurnali** (administrator).
- Formatlar: Excel (.xlsx), Word (.docx), PDF, CSV (UTF-8). Sana formati — dd.MM.yyyy.
- O'quvchi davomati tarixi — "Guruhlar va o'quvchilar" sahifasidagi **Davomat tarixi** tugmasi.

### 2.8. AI yordamchi
**Sozlash (administrator):** Sozlamalar → AI xizmati.
- *Claude (Anthropic)* yoki *OpenAI* — internet va API kaliti kerak (provayder saytida olinadi, pullik). Kalitni maydonga kiriting → **Saqlash** → **Ulanishni tekshirish**.
- *Lokal model (Ollama)* — ollama.com dan Ollama o'rnating, `ollama pull llama3.1` buyrug'i bilan model yuklang. Internet va kalit kerak emas, lekin kompyuter kuchli bo'lishi kerak (kamida 8–16 GB operativ xotira).
- **Soatlik limit** — bir soatda nechta so'rov yuborish mumkinligi (xarajatni nazorat qilish uchun).

**Ishlatish:** chap menyuda **AI yordamchi**.
1. Topshiriqni tanlang: mavzuni tushuntirish, dars ishlanmasi, maqsad va natijalar, test, turli darajadagi savollar, amaliy topshiriqlar, individual mashqlar, kodni tushuntirish/tekshirish, taqdimot rejasi, dars yakuni xulosasi, o'zlashtirish tahlili yoki erkin savol.
2. Guruh va fanni tanlasangiz, **KTR mavzulari** ro'yxatidan mavzu tanlash mumkin — soat, turi va kutilayotgan natija avtomatik qo'shiladi.
3. **Yuborish**. Javob kelgach, shu suhbatda aniqlashtiruvchi savol berishingiz mumkin.
4. **Tekshirish va tasdiqlash** — javobni tahrirlang va tasdiqlang. **Word'ga saqlash** faqat tasdiqlangan javob uchun ishlaydi; hujjatda "AI yordamida tayyorlandi, o'qituvchi tekshirdi" belgisi bo'ladi.

**Maxfiylik:** O'quvchilarning ism-familiyasini AI ga yozmang — matnda o'quvchi ismi uchrasa, dastur ogohlantiradi. "O'zlashtirish tahlili"da **Davomat ma'lumotini qo'shish (anonim)** tugmasi ismlarsiz statistikani (O'quvchi 1, 2, …) qo'shadi.

AI ishlamasa (internet yo'q, kalit noto'g'ri), dastur tushunarli xabar beradi — qolgan barcha bo'limlar odatdagidek ishlaydi.

### 2.9. Test va baholash
1. **Yangi test** → nom, fan, vaqt (daqiqa), variantlar soni, aralashtirish va **baholash mezoni** (standart: 5 — 86%, 4 — 71%, 3 — 56%) → **Saqlash**.
2. **Savollar** yorlig'i: savol matni, 2–6 ta variant, to'g'ri javobni belgilang, ball. Yoki **AI bilan yaratish** — AI savollari sariq rangda, "tekshirilmagan" bo'ladi: har birini o'qing, kerak bo'lsa tahrirlang va **Tasdiqlash** ni bosing.
3. **Tayyor deb belgilash** — shundan keyin natija kiritish mumkin. Natija kiritilgach savollarni o'zgartirib bo'lmaydi (kerak bo'lsa **Nusxa olish**).
4. **Variantlarni chop etish** — barcha variantlar Word yoki PDF ga, javoblar kaliti alohida faylga chiqadi.
5. **Natijalar va baholash** yorlig'ida guruh va sanani tanlang. Natija uch xil usulda kiritiladi:
   - qog'ozdagi javoblarni yozish: variant raqami va javoblar satri (masalan `BADC-A`, javobsiz — `-`) → **Kiritilgan javoblarni saqlash**;
   - **Import** — Excel/CSV: O'quvchi (raqami yoki F.I.Sh.), Variant, Javoblar;
   - **Kompyuterda topshirish** — tanlangan o'quvchi shu kompyuterda taymer bilan topshiradi, vaqt tugasa avtomatik topshiriladi.
6. Dastur ball, foiz va **avtomatik bahoni** hisoblaydi — bu faqat taklif. **Yakuniy** ustunida bahoni tekshiring va **Baholarni tasdiqlash** ni bosing. Avtomatik bahodan farq qilsa, sabab so'raladi va audit jurnaliga yoziladi.
7. **Hisobot** (Excel/Word/PDF) va **Savollar tahlili** — har bir savolga necha foiz o'quvchi to'g'ri javob bergani (juda qiyin yoki noaniq savollarni aniqlash uchun).

### 2.10. Dars rejimi
Chap menyuda **Dars rejimi** — dars paytida ishlatiladigan sahifa. Bugungi joriy (yoki keyingi) dars avtomatik tanlanadi.
- **Mavzu va materiallar** — KTR dagi mavzu, kutilayotgan natija, mavzuga biriktirilgan fayllar (taqdimot, PDF, video) va havolalar. Ikki marta bosish — ochish. **Mavzu o'tildi**, **Davomat olish** tugmalari shu yerda.
- **Ekranda ko'rsatish (proyektor)** — alohida katta oyna: uni proyektor/ikkinchi ekranga suring, **F11** — to'liq ekran, **Esc** — chiqish. Unda mavzu, taymer, topshiriq yoki tezkor so'rov ko'rsatiladi.
- **Taymer** — 3/5/10/15/20 daqiqa yoki o'zingiz kiritasiz; vaqt tugaganda signal beradi.
- **Topshiriq** — matnni yozing (yoki AI yordamchidan nusxa oling) va ekranga chiqaring.
- **Tezkor so'rov** — savol va 4 ta variant; o'quvchilar qo'l ko'taradi, siz "+" bilan sanaysiz; **Natijani ekranda ko'rsatish** — ustunli diagramma. **Jurnalga yozish** — natija dars jurnaliga qo'shiladi.
- **Jurnal va dars yakuni** — izoh, uyga vazifa, xulosa (**AI bilan xulosa tayyorlash** — tekshirib, tahrirlab saqlaysiz). **Dars o'tildi** va **Dars hisoboti (Word/PDF)**.

### 2.11. Ovozli yordamchi
**Sozlamalar → Ovozli yordamchi**: rejimni yoqing, nutqni aniqlash usulini tanlang:
- **OpenAI** — o'zbek tilini tushunadi; internet va OpenAI API kaliti kerak (Sozlamalar → AI xizmati da OpenAI ni tanlab kalitni saqlang).
- **Windows** — internetsiz, lekin faqat Windows'da o'rnatilgan tillar (odatda ingliz/rus).
**Mikrofonni sinash** va **Ovozni sinash** tugmalari bilan tekshiring.

Ishlatish: AI yordamchi va Dars jurnali sahifalarida **Gapirish** tugmasi — bosing, gapiring, yana bosing; matn maydonga qo'shiladi (yuborishdan oldin tekshiring). AI javobi ostida **O'qib berish**, yuqorida **Ovozni to'xtatish** / **Qayta eshittirish**. Mikrofon faqat tugma bosilganda ishlaydi, yozuv saqlanmaydi.

## 3. Zaxira nusxa
Dastur har kuni avtomatik nusxa oladi. **Zaxira nusxalar** bo'limida (administrator):
- **Shifrlangan nusxa (parol bilan)** — fleshka yoki boshqa joyga olib chiqish uchun tavsiya etiladi (AES-256). Parolni unutmang: usiz nusxani tiklab bo'lmaydi.
- **Tiklash** — oddiy (.db) yoki shifrlangan (.aupbak) nusxadan. Shifrlangan nusxa uchun parol so'raladi. Tiklashdan oldin joriy holat avtomatik saqlanadi.

## 4. Muammolar
- Xato chiqsa, xabarda jurnal fayli yo'li ko'rsatiladi: `%LOCALAPPDATA%\AiUstozPro\logs`.
- Parolni unutgan bo'lsangiz — administrator **Foydalanuvchilar → Parolni tiklash** orqali yangisini beradi.
