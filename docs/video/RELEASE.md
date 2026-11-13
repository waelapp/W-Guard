# نشر إصدار جديد

## الطريقة التلقائية (المفضلة)
1. في `src/AssemblyInfo.cs` ارفع الرقم، مثلاً: `AssemblyVersion("1.1.0.0")` و `AssemblyFileVersion("1.1.0.0")` و `AssemblyInformationalVersion("1.1")`.
2. ارفع التعديلات إلى GitHub.
3. أنشئ Tag جديداً مثل `v1.1` (من صفحة Releases ← Draft a new release ← اكتب الـ Tag واختر Create new tag ← Publish).
4. GitHub Actions يبني البرنامج وملف التثبيت من الكود ويرفق الملفات الأربعة بالإصدار تلقائياً.

## الطريقة اليدوية
1. شغّل `build.bat` - يخرج: `W-Guard-Setup.exe` و `W-Guard-Setup.exe.sha256` و `W-Guard.exe` و `W-Guard.exe.sha256`.
2. في GitHub: **Releases ← Draft a new release**، الـ Tag مثل `v1.1`، واكتب «ما الجديد» (يظهر للمستخدمين داخل نافذة التحديث).
3. اسحب الملفات الأربعة إلى المربع المنقّط السفلي «Attach binaries» (وليس مربع الوصف) ثم **Publish release**.

## كيف يصل التحديث للمستخدمين
كل نسخة تتحقق عند التشغيل، فتظهر نافذة «يتوفر إصدار جديد» بخطوتين:
1. «تنزيل التحديث» يفتح رابط الملف في المتصفح (ملف التثبيت للنسخة المثبتة، و `W-Guard.exe` للمحمولة).
2. المستخدم يشغّل الملف المنزّل فيثبّت الإصدار الجديد.

## بعد النشر: VirusTotal
1. ارفع `W-Guard-Setup.exe` و `W-Guard.exe` على https://www.virustotal.com
2. حدّث رابطي النتيجة في `docs/index.html` (سؤال «هل البرنامج آمن؟») و `README.md` بالبصمة الجديدة من ملفات `.sha256`.
