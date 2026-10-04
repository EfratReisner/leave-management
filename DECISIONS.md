<div dir="rtl">

# DECISIONS

> קצר וברור — חלק מההערכה.

## 1. החלטות ארכיטקטוניות
- **Backend:** הפרדה מינימלית — `LeaveRequestsController` דק (HTTP בלבד) ו‑`LeaveRequestService` ללוגיקה ולגישה ל‑DB. `ServiceResult<T>` ממפה שגיאות עסקיות בלי לכרוך את השירות ב‑ASP.NET.
- **למה בלי Repository / CQRS:** ל‑POC קטן זה over-engineering; `DbContext` בשירות מספיק ושומר על קריאות הקוד.
- **Frontend:** `LeaveRequestsService` + `EmployeesService`, מודלים עם `LeaveType` / `LeaveStatus`, ו‑`takeUntilDestroyed` למניעת memory leaks. הקומפוננטה אחראית על UI ו‑state בלבד.
- **מכסת ימים:** `AnnualQuota` חלה רק על `Vacation`. `Sick` / `Unpaid` בלי מכסה — בהתאם למודל ולדרישות המשימה.

## 2. הבאג ביתרת החופשה
- **מה היה הבאג:** חושב `used`, אבל הבדיקה הייתה `days > AnnualQuota` — בלי להוסיף ימים שכבר נוצלו. לכן בקשות שעולות יחד על המכסה עברו.
- **תיקון:** `used + days > AnnualQuota`, כולל בקשות `Pending` (לא רק `Approved`) וסינון לפי שנת ה‑`StartDate`. הלוגיקה ב‑`LeaveRequestService.Create`.
- **טסט:** `LeaveRequestsTests.Create_ExceedsRemainingBalance_ReturnsBadRequest`

## 3. אישור בקשה (approve) ו‑concurrency
- **Endpoint:** `POST /api/leave-requests/{id}/approve`
  - לא קיים → `404`
  - כבר `Approved` / `Rejected` → `409 Conflict`
  - חריגה ממכסת Vacation → `400`
- **Concurrency:** טרנזקציה + `SELECT … FOR UPDATE` על שורת העובד ב‑PostgreSQL לפני בדיקת היתרה והעדכון. סדר נעילות קבוע (קודם Employee) להקטנת deadlock.
- **DB:** פיתוח ובדיקות מול **SQLite** (בלי Docker). ב‑SQLite אין row locks כמו ב‑Postgres; מול PostgreSQL הנעילה היא האמיתית. טסט לוגי: `Approve_SecondRequestExceedingQuota_ReturnsBadRequest`.

## 4. על מה ויתרתי בגלל הזמן
- מניעת חפיפת תאריכים לאותו עובד (Create + Approve → 400).
- הצגת יתרת חופשה ב‑UI (מכסה / נוצל / נשאר).
- הודעות שגיאה מדויקות יותר בפרונט כשה‑API לא זמין או מחזיר גוף לא־מחרוזתי.

## 5. שימוש ב‑AI
נעזרתי ב‑**Cursor** לאורך העבודה (איתור באגים, מימוש, טסטים ותיעוד), עם בדיקה ושיקול דעת שלי על כל הצעה.

### איפה AI עזר (כולל prompts)
1. prompt: "קיים באג בבדיקת יתרת ימי החופשה — עובד יכול להגיש בקשה שחורגת מהמכסה. אתר את הבאג"  
   → הצביע על `used` שלא נכנס לתנאי. אימצתי `used + days` והוספתי טסט.
2. prompt: "משימה 2 — approve + concurrency בצורה מלאה"  
   → שלד ל‑endpoint, קודי שגיאה וטרנזקציה/`FOR UPDATE`. התאמתי לקוד ולטסטים.
3. prompt: "משימה 6 — service layer, טיפוסים, RxJS בלי memory leaks"  
   → בניית שירותי Frontend ו‑`takeUntilDestroyed`. הקומפוננטה נשארה אחראית על UI.

### איפה דחיתי/תיקנתי הצעה של AI
- AI הציע להוסיף שכבת Repository (גישה נפרדת ל‑DB). דחיתי — ל‑POC עם לוגיקה מצומצמת זה מסבך בלי תועלת מספקת; נשארתי עם Controller דק + Service אחד שמשתמש ישירות ב‑`DbContext`, בהתאם ל‑"בלי over-engineering".
- בתיקון היתרה, מעבר לתיקון הבסיסי `used + days`, הרחבתי לספור גם בקשות `Pending` (לא רק `Approved`) — אחרת אפשר להגיש כמה בקשות ממתינות שביחד חורגות מהמכסה.

### אבטחה
- ב‑`Search` הישן: SQL עם שרשור של `name` → סיכון SQL injection. הוחלף ב‑LINQ עם פרמטרים ב‑`LeaveRequestService.SearchByEmployeeName`. השינוי ב‑API; ה‑UI הנוכחי לא חושף שדה חיפוש חופשי.

## 6. הוראות הרצה
עבדתי מול **SQLite** (בלי Docker). נדרש .NET 8 ו‑Node 18+.

```powershell
# API
cd backend
$env:Database__Provider="Sqlite"
dotnet run --project src/LeaveManagement.Api
# http://localhost:5080/swagger

# Tests (לעצור את ה‑API קודם אם הוא רץ)
$env:Database__Provider="Sqlite"
dotnet test

# Frontend
cd frontend
npm install
npm start
# http://localhost:4200
```

כשה‑API לא רץ, הפרונט מציג שגיאה גנרית בהגשה — צריך גם את פורט 5080.

</div>
