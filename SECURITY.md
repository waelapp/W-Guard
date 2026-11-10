# الأمان / Security

- نزّل W-Guard من [صفحة الإصدارات الرسمية](https://github.com/waelapp/W-Guard/releases) فقط.
- للتأكد من الملف: قارن بصمة SHA-256 للملف المنزّل بالموجودة في `W-Guard.exe.sha256` في نفس الإصدار:
  ```
  powershell Get-FileHash W-Guard.exe -Algorithm SHA256
  ```
- للإبلاغ عن ثغرة أمنية: استخدم زر **Report a vulnerability** في تبويب Security بالمستودع بدلاً من فتح Issue عام.

Download only from the official Releases page and verify the SHA-256. Report security issues privately with "Report a vulnerability" in the repository Security tab.
