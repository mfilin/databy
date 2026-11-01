# Databy Reports

Прототип настольного клиента миграции отчётов Delphi → .NET/Avalonia по `TZ_migration_delphi_to_dotnet.docx`.

## Запуск

1. Запустите PostgreSQL на `localhost:5432`.
2. Если база пустая, примените демо DDL: `psql -h localhost -U db1 -d db1 -f db/demo.sql` (скрипт пересоздаёт только демонстрационные строки `reports.demo_sales`). В текущем окружении данные уже загружены в контейнер `local-postgres`, база `db1`.
3. Установите .NET 8 SDK.
4. Запустите `dotnet run --project src/Databy.Reports`.

Подключение читается из локального `src/Databy.Reports/appsettings.local.json` или переменной `DATABY_CONNECTION`. Локальный файл исключён из Git.

Каталог YAML лежит в `catalog/`. Демо-отчёт вызывает проверенную в `pg_proc` функцию `reports.fn_sales` с параметрами Npgsql. Колонки строятся по схеме БД и переопределениям YAML. Превью ограничено 5000 строками; полная выдача стадируется во временный CSV для экспорта. Логи записываются в `logs/`.

## Тесты

- `tests/Tests.Unit` — каталог и экспорт (T1).
- `tests/Tests.Integration` — PostgreSQL Testcontainers (T2).
- команды и покрытие: [tests/README.md](tests/README.md).

## Состав

- `src/Databy.Reports` — Avalonia UI, загрузчик каталога и экспорт XLSX/CSV.
- `catalog/` — YAML-каталог и демо-отчёт.
- `db/demo.sql` — таблица, тестовые продажи и функция отчёта.
- `tests/db/init.sql` — синтетическая схема и функции для интеграционных тестов.

Приложение пока не реализует полную локальную авторизацию и роли, журнал аудита, зависимые справочники, schema JSON и потоковый XLSX с контролем памяти. Эти пункты остаются за пределами текущего тестового среза.
