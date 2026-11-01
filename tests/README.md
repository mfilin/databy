# Тесты

Проекты привязаны к уровням из ТЗ:

- `Tests.Unit` — T1: проверки каталога, XLSX/CSV и headless smoke Avalonia; XLSX проверяет независимая от ClosedXML библиотека ExcelDataReader.
- `Tests.Integration` — T2: PostgreSQL в Testcontainers; тесты используют только синтетические SQL-данные из `tests/db/init.sql`.
- `.github/workflows/tests.yml` запускает T1 на Linux и Windows, T2 — на Linux.

Команды из корня репозитория:

```powershell
dotnet test tests/Tests.Unit --filter Category=T1
dotnet test tests/Tests.Integration --filter Category=T2
```

T3 (граница в 1 048 575/1 048 576 строк, замер пикового потребления памяти и smoke опубликованных win-x64/linux-x64 сборок) пока не входит в быстрый набор. DB-02 для PROCEDURE/refcursor и проверки безопасности SEC-01…SEC-05 ожидают реализации соответствующих модулей приложения.
