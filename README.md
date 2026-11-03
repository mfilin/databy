# Databy Reports

Прототип настольного клиента миграции отчётов Delphi → .NET/Avalonia по `TZ_migration_delphi_to_dotnet.docx`.

## Запуск

1. Запустите PostgreSQL на `localhost:5432`.
2. Если база пустая, примените демо DDL: `psql -h localhost -U db1 -d db1 -f db/demo.sql` (скрипт пересоздаёт только демонстрационные строки `reports.demo_sales`). В текущем окружении данные уже загружены в контейнер `local-postgres`, база `db1`.
3. Установите .NET 8 SDK.
4. Соберите и запустите:
   - `dotnet build Databy.Reports.sln`
   - Windows: `build\Databy.Reports\Debug\net8.0\Databy.Reports.exe`
   - Linux/macOS: `./build/Databy.Reports/Debug/net8.0/Databy.Reports`

Все проекты собираются в корневую папку `build/<ИмяПроекта>/` (настраивается в `Directory.Build.props`, пути кроссплатформенные), поэтому исполняемые файлы всегда в одном месте. Запуск без хардкода пути тоже доступен через `dotnet run --project src/Databy.Reports`.

Подключение читается из локального `src/Databy.Reports/appsettings.local.json` или переменной `DATABY_CONNECTION`. Локальный файл исключён из Git.

Каталог YAML лежит в `catalog/`. Демо-отчёт вызывает проверенную в `pg_proc` функцию `reports.fn_sales` с параметрами Npgsql. Колонки строятся по схеме БД и переопределениям YAML. Превью ограничено 5000 строками; полная выдача стадируется во временный CSV для экспорта. Логи записываются в `logs/`.

## Тесты

- `tests/Tests.Unit` — каталог и экспорт (T1).
- `tests/Tests.Integration` — PostgreSQL Testcontainers (T2).
- команды и покрытие: [tests/README.md](tests/README.md).

## Архитектура

Приложение использует MVVM и внедрение зависимостей (`Microsoft.Extensions.DependencyInjection`).

- `src/Databy.Reports/Composition/ServiceRegistration.cs` — регистрация сервисов и ViewModel.
- `src/Databy.Reports/Services/` — конфигурация, каталог, PostgreSQL-запросы, экспорт, диалоги (`IApplicationSettings`, `IReportCatalogService`, `IReportQueryService`, `IReportExportService`, `IFileDialogService`).
- `src/Databy.Reports/ViewModels/` — `MainWindowViewModel`, `ReportTabViewModel`, `CatalogNode`, `ReportColumnViewModel` на `CommunityToolkit.Mvvm`.
- `src/Databy.Reports/Views/MainWindow.axaml` — светлый интерфейс: Header, Body (левое дерево каталога, вкладки и таблица с пагинацией, правая панель фильтров), StatusBar.

## Состав

- `src/Databy.Reports` — Avalonia UI, сервисы каталога/БД/экспорта и MVVM-слой.
- `catalog/` — YAML-каталог и демо-отчёт.
- `db/demo.sql` — таблица, тестовые продажи и функция отчёта.
- `tests/db/init.sql` — синтетическая схема и функции для интеграционных тестов.

Приложение пока не реализует полную локальную авторизацию и роли, журнал аудита, зависимые справочники, schema JSON и потоковый XLSX с контролем памяти. Эти пункты остаются за пределами текущего тестового среза.
