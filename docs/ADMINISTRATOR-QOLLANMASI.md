# AI Ustoz Pro — administrator qo'llanmasi (v0.1)

## Rollar
| Rol | Huquqlar |
|---|---|
| Administrator | Hamma narsa: foydalanuvchilar, zaxira/tiklash, audit jurnali, davomat yozuvini o'chirish |
| O'qituvchi | Guruhlar, o'quvchilar, fanlar, kalendar, jadval, dars sanalari, KTR, davomat (faqat o'z darslarida), hisobotlar |
| Kuzatuvchi (rahbar) | Faqat ruxsat berilgan guruhlar hisobotlarini ko'rish va eksport qilish |

Kuzatuvchiga guruhlar **Foydalanuvchilar** sahifasida, foydalanuvchini tanlab, belgilanadi.

## Foydalanuvchilar
- Yaratish: login (kamida 3 belgi, lotin harflari/raqam/`.`/`_`/`-`), F.I.Sh., rol, boshlang'ich parol.
- Bloklash: foydalanuvchi kira olmaydi, ma'lumotlari saqlanadi. Oxirgi faol administratorni bloklab bo'lmaydi.
- 5 marta noto'g'ri parol → 15 daqiqa qulf. Parolni tiklash qulfni ham ochadi.

## Ma'lumotlar joylashuvi
```
%LOCALAPPDATA%\AiUstozPro\
  data\aiustoz.db      — SQLite ma'lumotlar bazasi
  backups\             — zaxira nusxalar (avtomatik: oxirgi 14 ta)
  logs\                — xatolar jurnali
  exports\             — vaqtinchalik eksportlar
```
Dasturni o'chirish (uninstall) bu papkani **o'chirmaydi**. Yangi versiya o'rnatilganda baza avtomatik yangilanadi; yangilashdan oldin `backups\before-upgrade-*.db` nusxasi olinadi.

## Zaxira va tiklash
- Qo'lda: **Zaxira nusxalar → Zaxira nusxa olish** yoki **Boshqa joyga saqlash** (fleshka, tarmoq disk).
- Tiklash: fayl `PRAGMA integrity_check` va jadval tuzilishi bo'yicha tekshiriladi; joriy holat `before-restore` nusxasi sifatida saqlanadi; so'ng dastur qayta ishga tushadi.
- **Diqqat:** v0.1 da zaxira fayllari shifrlanmagan. Ularni faqat ishonchli joyda saqlang (qarang: CHEKLOVLAR).

## Audit jurnali
Barcha o'zgartirishlar (kim, qachon, nima, sabab) yoziladi. **Hisobotlar → Audit jurnali** orqali davr bo'yicha Excel/PDF ga chiqariladi.

## Boshqa kompyuterga ko'chirish
1. Eski kompyuterda zaxira nusxani fleshkaga saqlang.
2. Yangi kompyuterda dasturni o'rnating, administrator yarating, **Zaxira nusxalar → Tiklash** orqali faylni tanlang.

## Buyruq qatori parametrlari
- `--data-dir <papka>` — boshqa ma'lumotlar papkasidan foydalanish (masalan, sinov uchun).
- `--smoke-test --data-dir <papka>` — interfeyssiz uchidan-uchigacha tekshiruv; natija `<papka>\smoke-test.log`, chiqish kodi 0 — muvaffaqiyat.
