# PV Designer — تصميم ومحاكاة محطات الطاقة الشمسية ثلاثية الأبعاد مع الواقع الافتراضي

برنامج احترافي مبني على **Unity 6** لتصميم أنظمة الطاقة الشمسية الكهروضوئية (على غرار PVsyst):
رسم الموقع ثلاثي الأبعاد، توزيع الألواح آلياً، حساب التظليل بتتبع الأشعة، تصميم السلاسل والانفرترات،
محاكاة ساعية لإنتاج الطاقة (8760 ساعة) مع مخطط الخسائر، تقرير احترافي، دعم **نظارات الواقع الافتراضي (OpenXR)**،
و**تحديث تلقائي** للعملاء عبر إصدارات GitHub.

> English summary below.

## المميزات

| المجال | التفاصيل |
|---|---|
| الموقع | إحداثيات، ارتفاع، منطقة زمنية، ألبيدو، مدن جاهزة (السعودية، الخليج، مصر، المغرب...)، رسم حدود الأرض بالماوس أو مستطيل بالأبعاد، مسافة الارتداد |
| الأرصاد | تحميل سنة نموذجية TMY من **PVGIS** مباشرة، أو توليد سنة تقريبية بدون إنترنت، جدول شهري |
| المكونات | مكتبة ألواح وانفرترات عامة + **استيراد ملفات PVsyst ‏.PAN و.OND** |
| التصميم | حقول متعددة، طاولات ثابتة بأي ميل/اتجاه، **متتبع أحادي المحور مع Backtracking**، Portrait/Landscape، Pitch و GCR، أسطح مباني (ارتفاع القاعدة)، توزيع آلي يتجنب العوائق |
| العوائق | مبانٍ، أشجار (تاج شبه شفاف)، جدران، أعمدة، خزانات — سحب، تدوير، تكرار، حذف |
| التظليل | تتبع أشعة ثلاثي الأبعاد (صف لصف + العوائق)، جدول عامل التظليل حسب السمت/الارتفاع، عامل تظليل الإشعاع المنتشر والألبيدو، وضع **خطي** و**كهربائي (حسب السلاسل)**، خريطة حرارية للتظليل في المشهد |
| الكهرباء | عدد الألواح في السلسلة مع فحص Voc على البرودة و Vmpp على الحرارة، عدد الانفرترات حسب نسبة DC/AC، فحص التيارات، تلوين السلاسل حسب الانفرتر |
| المحاكاة | موقع الشمس NOAA، نقل الإشعاع Perez / Hay-Davies، IAM، الاتساخ الشهري، النموذج الحراري Faiman، خسائر الإشعاع المنخفض والحرارة، الجودة، LID، عدم التطابق، الأسلاك DC/AC، كفاءة الانفرتر والقص، المحول، التوقف |
| النتائج | الطاقة السنوية، الإنتاج النوعي kWh/kWp، نسبة الأداء PR، جدول شهري، مخطط الخسائر بأسلوب PVsyst، إنتاج 25 سنة |
| الاقتصاد | التكلفة، LCOE، ‏NPV، ‏IRR، فترة الاسترداد |
| التقارير | تقرير HTML جاهز للطباعة/PDF مع صورة المشهد ورسوم بيانية + قائمة مواد BOM + تصدير CSV ساعي |
| الواقع الافتراضي | Meta Quest (Link / Air Link أو تطبيق APK مستقل)، Valve Index، HTC Vive، Pico، WMR — نموذج مصغر على طاولة أو مشي بالحجم الحقيقي |
| التحديث التلقائي | يفحص البرنامج آخر إصدار على GitHub عند التشغيل، يحمّل، يتحقق من **SHA-256** ثم يثبّت ويعيد التشغيل |

## البدء السريع (للمطوّر)

1. ثبّت **Unity Hub** ثم المحرر **6000.0.83f1 (Unity 6 LTS)** مع الوحدات:
   *Windows Build Support (Mono)* و *Android Build Support* (لـ Quest).
2. افتح مجلد المشروع من Unity Hub. عند السؤال عن تفعيل Input System اختر **Yes** (البرنامج يدعم النظامين).
3. من القائمة: **PV Designer ▸ Setup ▸ Configure project** (ينشئ المشهد ويضبط OpenXR تلقائياً).
4. اضغط **Play**. البرنامج يبني كل شيء بالكود (لا يحتاج إعداد مشاهد يدوياً).
5. للبناء: **PV Designer ▸ Build ▸ Windows x64** أو **Meta Quest**.

اختبارات الحسابات تعمل بدون Unity:

```bash
dotnet test Tools/CoreTests
```

## الاستخدام

1. **Site**: اختر المدينة أو أدخل الإحداثيات، ثم ارسم حدود الأرض (أداة *Site boundary*) أو أنشئ مستطيلاً.
2. **Meteo**: اضغط *Download PVGIS TMY*.
3. **Components**: اختر اللوح والانفرتر أو استورد ملفات PAN/OND من مجلد `Documents/PV Designer/Components`.
4. **Layout**: حدد الميل والاتجاه والـ Pitch وأبعاد الطاولة ثم *Generate layout*.
5. **Obstacles**: أضف المباني والأشجار حول الموقع.
6. **Electrical**: راجع السلاسل والانفرترات والتحذيرات.
7. **Shading / Losses**: اضبط نموذج التظليل والخسائر.
8. اضغط **Simulate** ثم **Report** لتصدير التقرير.

اختصارات: `F` تأطير الموقع، `T` منظر علوي، `Space` تشغيل حركة الشمس، `Ctrl+Z / Ctrl+Y` تراجع/إعادة، `Ctrl+S` حفظ،
`Delete` حذف العنصر المحدد، `R` تدوير العائق، زر الفأرة الأيمن للدوران، الأوسط للتحريك، العجلة للتكبير.

### التحكم بالواقع الافتراضي

| الزر | الوظيفة |
|---|---|
| زناد اليمين | ضغط أزرار اللوحة / الانتقال (Teleport) في وضع المشي |
| Grip (يد واحدة) | تحريك النموذج — (يدين) تكبير/تصغير وتدوير |
| A | التبديل بين نموذج الطاولة والمشي بالحجم الحقيقي |
| B | إعادة اللوحة أمامك |
| Y / Menu | إظهار/إخفاء لوحة التحكم |
| X | تشغيل/إيقاف حركة الشمس |
| عصا اليسار | الساعة (يمين/يسار) والتاريخ (أعلى/أسفل) |
| عصا اليمين | الدوران 30° أو تمرير اللوحة |

## نشر التحديثات للعملاء

راجع [docs/RELEASING.md](docs/RELEASING.md). باختصار: أضف أسرار Unity في GitHub ثم:

```bash
git tag v1.0.0
git push origin v1.0.0
```

سيقوم GitHub Actions ببناء نسختي Windows و Quest ونشر إصدار يحتوي على
`PVDesigner-Windows-x64.zip` و `PVDesigner-Quest.apk` مع ملفات `.sha256`. البرامج المثبتة لدى العملاء ستكتشف الإصدار وتعرض التحديث.

> **مهم:** يجب أن يكون المستودع (أو مستودع الإصدارات) **عاماً** لكي يتمكن البرنامج لدى العملاء من قراءة الإصدارات بدون كلمة مرور.
> يمكن تغيير المستودع في `Assets/PVDesigner/Scripts/Runtime/Updates/AutoUpdater.cs` ‏(`UpdateConfig`).

## بنية المشروع

```
Assets/PVDesigner/Scripts/
  Core/        حسابات مستقلة عن Unity (قابلة للاختبار): الشمس، الإشعاع، الأرصاد، التوزيع،
               التظليل، الكهرباء، المحاكاة، الاقتصاد، التقارير، التحديثات، الحفظ
  Runtime/     تطبيق Unity: المشهد ثلاثي الأبعاد، الأدوات، الواجهة، الواقع الافتراضي، المحدّث
  Editor/      إعداد المشروع والبناء للـ CI
Tools/CoreTests/  اختبارات xUnit (بيانات PVGIS حقيقية للرياض)
.github/workflows/ اختبارات + بناء ونشر الإصدارات
docs/          توثيق النشر والمعمارية
```

---

## English summary

**PV Designer** is a Unity 6 application for professional PV plant design, similar in scope to PVsyst's 3D near-shading
scene plus its hourly simulation: site & boundary drawing, PVGIS TMY import, PAN/OND import, automatic layout of fixed
tables or single-axis trackers (with backtracking), ray-traced near shading (linear and string-wise), string/inverter sizing,
8760-hour simulation with a PVsyst-style loss diagram, economics, HTML report / CSV export, OpenXR VR (table-top and
1:1 walk modes) and GitHub-Releases based auto-update with SHA-256 verification.

* Open with Unity **6000.0.83f1**, run *PV Designer ▸ Setup ▸ Configure project*, press Play.
* Core unit tests: `dotnet test Tools/CoreTests` (no Unity license needed).
* Release: push a tag `vX.Y.Z` — see [docs/RELEASING.md](docs/RELEASING.md).
* Architecture notes: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

Validation: for Riyadh (fixed 25°, PVGIS TMY) the simulator gives 2395 kWh/m² in-plane irradiation
(PVGIS: 2417) and ~1890 kWh/kWp with PR ≈ 0.79 for a 2 MWp plant — consistent with PVsyst-type results.
Results are engineering estimates; bankable studies should be cross-checked with measured data.
