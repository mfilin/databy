# Databy Reports

Прототип настольного клиента миграции отчётов Delphi → .NET/Avalonia по [`docs/TZ_migration_delphi_to_dotnet.docx`](docs/TZ_migration_delphi_to_dotnet.docx).

## Требования

- .NET 8 SDK (версии пакетов не меняются).
- PostgreSQL, доступный по строке подключения. Неявного подключения нет: без `DATABY_CONNECTION` и без `appsettings.local.json` приложение запускается, но запуск отчёта завершится ошибкой с инструкцией, где задать строку подключения.
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

## Колонки отчёта

Набор и порядок колонок берутся из ответа процедуры (порядок БД). В YAML описываются только скрытие и оформление; колонку, не упомянутую в YAML, показывать не нужно — она появится автоматически.

```yaml
result:
  hide: [internal_id, "ОГРН"]
  columns:
    name_: { title: Краткое наименование }
```

- `result.hide` — список имён колонок из БД, которые нужно скрыть (сравнение точное, с учётом регистра, пробелов и кириллицы).
- `result.columns.<имя>.hidden: true` — равнозначный способ скрыть колонку.
- `title` и `format` задаются только для видимых колонок; без `title` заголовок равен имени колонки.
- Если в `hide` указано имя, которого нет в результате, пишется предупреждение в лог, отчёт продолжает работать.
- Если скрыты все колонки, запуск завершается ошибкой «Все колонки скрыты в YAML отчёта», файл не создаётся.

Правила одинаково применяются к таблице на экране и к экспорту (XLSX и CSV).

## Отчёты по списку значений

Отчёт может принимать на вход список значений (например, список ИНН). Контракт описывается необязательной секцией `input`; отчёты без неё работают как обычно.

```yaml
code: org.by_inn
title: Организации по списку ИНН
routine: { name: reports.fn_orgs_by_inn, kind: function, timeout_sec: 600 }
parameters:                     # обычные параметры отчёта
  - { name: p_asof, type: date, label: Дата, required: true, default: today }
input:                          # описание списка значений
  label: Список ИНН             # по умолчанию «Список значений»
  max_rows: 1000000             # по умолчанию и максимум 1 000 000
  batch_size: 100000            # по умолчанию 100 000, 1..max_rows
  row_id_parameter: p_row_id    # параметр-массив идентификаторов строк
  result_key: row_id            # колонка результата для сопоставления строк
  status_column: "Статус поиска"# по умолчанию «Статус поиска», не равен result_key
  columns:                      # обязательна хотя бы одна колонка
    - { name: p_inn,  type: string, label: ИНН,  required: true }
    - { name: p_date, type: date,   label: Дата, required: true }
```

Правила загрузчика:

- `columns` непустой; имена колонок и `row_id_parameter` — идентификаторы (`^[a-zA-Z_][a-zA-Z0-9_]*$`);
- имена колонок уникальны и не совпадают с именами обычных `parameters`;
- `result_key` не пуст, `status_column` не равен `result_key`;
- типы колонок: `string`, `int`, `decimal`, `date`, `bool`;
- `1 ≤ batch_size ≤ max_rows`, `1 ≤ max_rows ≤ 1 000 000`;
- `routine.kind` — только `function`.

Нарушение любого правила — ошибка файла: отчёт не попадает в каталог, остальные отчёты загружаются. Запуск отчётов по списку пока не реализован — секция только валидируется.

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

## Логи и аудит

Каталог `logs/` создаётся автоматически рядом с исполняемым файлом.

- `logs/databy-YYYYMMDD.log` — общий журнал приложения.
- `logs/audit-YYYYMMDD.log` — аудит запусков отчётов.

В аудит попадают: учётная запись ОС (`DOMAIN\User` на Windows), код и название отчёта, параметры запуска, результат (`started`, `succeeded`, `failed`, `canceled`), количество строк и длительность. Для ошибок также пишется тип и текст исключения. Общий журнал хранится 30 дней, аудит — 90 дней.

Пока локальная авторизация не реализована, пользователь определяется по учётной записи ОС. Для перехода на авторизацию приложения достаточно заменить реализацию `ICurrentUser`.

## Тесты

- `tests/Tests.Unit` — каталог и экспорт (T1), запускается на Windows и Linux.
- `tests/Tests.Integration` — PostgreSQL Testcontainers (T2), требует Docker.
- команды и покрытие: [tests/README.md](tests/README.md).

## Архитектура

Приложение использует MVVM и внедрение зависимостей (`Microsoft.Extensions.DependencyInjection`).

- `src/Databy.Reports/Composition/ServiceRegistration.cs` — регистрация сервисов и ViewModel.
- `src/Databy.Reports/Services/` — конфигурация, каталог, PostgreSQL-запросы, экспорт, диалоги и аудит (`IApplicationSettings`, `IReportCatalogService`, `IReportQueryService`, `IReportExportService`, `IFileDialogService`, `ICurrentUser`, `IReportAuditService`).
- `src/Databy.Reports/ViewModels/` — `MainWindowViewModel`, `ReportTabViewModel`, `CatalogNode`, `ReportColumnViewModel` на `CommunityToolkit.Mvvm`.
- `src/Databy.Reports/Views/MainWindow.axaml` — светлый интерфейс: Header, Body (левое дерево каталога, вкладки и таблица с пагинацией, правая панель фильтров), StatusBar.

## Состав

- `src/Databy.Reports` — Avalonia UI, сервисы каталога/БД/экспорта и MVVM-слой.
- `catalog/` — YAML-каталог и демо-отчёты.
- `db/demo.sql` — таблица, тестовые продажи и функции отчётов.
- `tests/db/init.sql` — синтетическая схема и функции для интеграционных тестов.
- `docs/` — техническое задание.

Приложение пока не реализует полную локальную авторизацию и роли, зависимые справочники, schema JSON и потоковый XLSX с контролем памяти. Аудит запусков отчётов уже пишется в `logs/audit-YYYYMMDD.log`, но использует учётную запись ОС, а не пользователя приложения.