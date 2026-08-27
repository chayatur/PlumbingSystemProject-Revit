// הפעלת <UseWPF>true</UseWPF> (נדרשת לתשתית-ההתקדמות - ראו
// docs/progress-infrastructure.md) משנה איזה namespaces ה-SDK מייצר
// אוטומטית כ-ImplicitUsings, ומשמיטה בשקט את System.IO (Path/Directory/
// File) שהמון קבצי-פקודה קיימים כבר הסתמכו עליו במרומז. השחזור כאן הוא
// התיקון הקטן-ביותר האפשרי - אפס שינוי-התנהגות, ואפס נגיעה בקוד בפועל
// של אף קובץ-פקודה קיים.
global using System.IO;
