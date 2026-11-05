# Databy Reports

Прототип настольного клиента миграции отчётов Delphi → .NET/Avalonia по [`docs/TZ_migration_delphi_to_dotnet.docx`](docs/TZ_migration_delphi_to_dotnet.docx).

## Требования

- .NET 8 SDK (версии пакетов не меняются).
- PostgreSQL, доступный по строке подключения (по умолчанию `localhost:5432`).
- Linux: библиотеки для Avalonia/X11 и шрифтов. Для Debian/Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y libx11-6 libice6 libsm6 libfontconfig1 libfreetype6 \
  libxrandr2 libxi6 libxcursor1 libxinerama1 libgl1 libegl1 libxext6
```

Для запуска GUI на headless-сервере нужен X-сервер (например, `xvfb-run`).

## Установка

Логи (`logs/`), файл `appsettings.local.json` и каталог `catalog/` располагаются **рядом с исполняемым файлом**: приложение читает их из `AppContext.BaseDirectory`. Поэтому ставить программу нужно в каталог, где у пользователя есть право записи:

- Windows: `%LOCALAPPDATA%\Programs\Databy` (например, `C:\Users\<user>\AppData\Local\Programs\Databy`);
- Linux: `~/Databy` или `~/.local/opt/Databy`.

Установка в `C:\Program Files` и `/opt` **не поддерживается**: каталог доступен только на чтение, и приложение не сможет создавать `logs/` и `appsettings.local.json`.

Установка:

1. Опубликуйте сборку (см. «Публикация») или распакуйте готовый архив.
2. Скопируйте содержимое `publish/<rid>` в выбранный каталог.
3. При необходимости создайте рядом с исполняемым файлом `appsettings.local.json` (см. «Настройка подключения»).
4. Запустите `Databy.Reports.exe` (Windows) или `./Databy.Reports` (Linux).

Обновление: остановите приложение, замените файлы сборки, сохранив `logs/`, `appsettings.local.json` и `catalog/`.

## Настройка подключения

Строка подключения берётся в следующем порядке:

1. Переменная окружения `DATABY_CONNECTION`.
2. Файл `appsettings.local.json` в каталоге приложения (`AppContext.BaseDirectory`):
   - для установленной программы — рядом с исполняемым файлом, например `%LOCALAPPDATA%\Programs\Databy\appsettings.local.json` или `~/Databy/appsettings.local.json`;
   - при `dotnet run`/`dotnet build` — в каталоге сборки: `build/Databy.Reports/Debug/net8.0/appsettings.local.json`. Файл копируется туда из `src/Databy.Reports/appsettings.local.json` (исключён из Git).

Пример:

```json
{
  "ConnectionString": "Host=localhost;Port=5432;Database=db1;Username=db1;Password=..."
}
```

Демо-данные (если база пустая):

```bash
psql -h localhost -U db1 -d db1 -f db/demo.sql
```

## Сборка и запуск

```bash
dotnet build Databy.Reports.sln
dotnet run --project src/Databy.Reports
```

Все проекты собираются в корневую папку `build/<ИмяПроекта>/` (задаётся в `Directory.Build.props` через `$([MSBuild]::NormalizeDirectory(...))`, поэтому путь одинаков на Windows и Linux).

Готовые сборки после `dotnet build`:

- Windows: `build\Databy.Reports\Debug\net8.0\Databy.Reports.exe`
- Linux: `./build/Databy.Reports/Debug/net8.0/Databy.Reports`

## Публикация

Windows (x64):

```powershell
dotnet publish src/Databy.Reports -c Release -r win-x64 --self-contained -o publish/win-x64
```

Linux (x64):

```bash
dotnet publish src/Databy.Reports -c Release -r linux-x64 --self-contained -o publish/linux-x64
```

Исполняемые файлы: `publish/win-x64/Databy.Reports.exe` и `publish/linux-x64/Databy.Reports` (на Linux выдайте право на запуск: `chmod +x publish/linux-x64/Databy.Reports`).

## Тесты

- `tests/Tests.Unit` — каталог и экспорт (T1), запускается на Windows и Linux.
- `tests/Tests.Integration` — PostgreSQL Testcontainers (T2), требует Docker.
- команды и покрытие: [tests/README.md](tests/README.md).

## Архитектура

Приложение использует MVVM и внедрение зависимостей (`Microsoft.Extensions.DependencyInjection`).

- `src/Databy.Reports/Composition/ServiceRegistration.cs` — регистрация сервисов и ViewModel.
- `src/Databy.Reports/Services/` — конфигурация, каталог, PostgreSQL-запросы, экспорт, диалоги (`IApplicationSettings`, `IReportCatalogService`, `IReportQueryService`, `IReportExportService`, `IFileDialogService`).
- `src/Databy.Reports/ViewModels/` — `MainWindowViewModel`, `ReportTabViewModel`, `CatalogNode`, `ReportColumnViewModel` на `CommunityToolkit.Mvvm`.
- `src/Databy.Reports/Views/MainWindow.axaml` — светлый интерфейс: Header, Body (левое дерево каталога, вкладки и таблица с пагинацией, правая панель фильтров), StatusBar.

## Состав

- `src/Databy.Reports` — Avalonia UI, сервисы каталога/БД/экспорта и MVVM-слой.
- `catalog/` — YAML-каталог и демо-отчёты.
- `db/demo.sql` — таблица, тестовые продажи и функции отчётов.
- `tests/db/init.sql` — синтетическая схема и функции для интеграционных тестов.
- `docs/` — техническое задание.

Приложение пока не реализует полную локальную авторизацию и роли, журнал аудита, зависимые справочники, schema JSON и потоковый XLSX с контролем памяти. Эти пункты остаются за пределами текущего тестового среза.