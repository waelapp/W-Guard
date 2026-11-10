# نشر إصدار جديد

1. في `src/AssemblyInfo.cs` ارفع الرقم، مثلاً: `AssemblyVersion("1.1.0.0")` و `AssemblyFileVersion("1.1.0.0")` و `AssemblyInformationalVersion("1.1")`.
2. شغّل `build.bat` - يخرج 4 ملفات: `W-Guard-Setup.exe` و `W-Guard-Setup.exe.sha256` (ملف التثبيت) و `W-Guard.exe` و `W-Guard.exe.sha256` (النسخة المحمولة).
   ملف التثبيت يحتاج [Inno Setup 6](https://jrsoftware.org/isdl.php) مثبتاً على جهازك.
3. ارفع تعديلات الكود إلى GitHub.
4. في المستودع: **Releases ← Draft a new release**
   - Tag: `v1.1`
   - Title: `W-Guard 1.1`
   - الوصف: ما الجديد (يظهر للمستخدمين داخل نافذة التحديث)
   - ارفع الملفات الأربعة
5. اضغط **Publish release**.

كل نسخة مثبّتة تتحقق عند التشغيل وتظهر لها نافذة "يتوفر إصدار جديد".
النسخ المثبّتة تتحدث عبر `W-Guard-Setup.exe` بصمت، والنسخة المحمولة تستبدل نفسها بـ `W-Guard.exe`.
لا تحذف ملفات `.sha256` - بدونها يرفض البرنامج التحديث.

## بعد النشر: VirusTotal
1. ارفع `W-Guard-Setup.exe` و `W-Guard.exe` على https://www.virustotal.com
2. البصمة الجديدة موجودة في ملفات `.sha256`. حدّث الرابطين في `docs/index.html` و `README.md`:
   `https://www.virustotal.com/gui/file/<البصمة>`
