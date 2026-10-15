# Архитектура проекта (TypoErp)

## 1. Технологический стек
- **Runtime & SDK:** .NET 10 (`net10.0`), C#
- **UI Framework:** Blazor Web App (Interactive Server Render Mode) + **MudBlazor**
- **Database:** PostgreSQL + EF Core 10 (`Npgsql.EntityFrameworkCore.PostgreSQL`)
- **File Hashing:** `System.IO.Hashing` (`XxHash64`) — сверхбыстрое потоковое хэширование тяжелых файлов типографии
- **Authentication & Authorization:** ASP.NET Core Cookie/Identity + гранулярные разрешения (Permissions), делегируемые администратором
- **Logging:** Serilog (`Serilog.AspNetCore`, вывод в консоль и ротируемые файлы)
- **Testing:** xUnit, NSubstitute, Shouldly / FluentAssertions, bUnit

## 2. Структура Solution (Модульный Монолит)
```text
TypoErp/
├── TypoErp.sln (или TypoErp.slnx)
├── docs/
│   └── ai/
│       ├── 00_AI_RULES.md
│       ├── 01_ARCHITECTURE.md
│       ├── 02_DOMAIN_PRODUCTS.md
│       └── 03_ROADMAP_AND_STATE.md
├── scripts/
│   └── pack-context.sh
├── src/
│   ├── TypoErp.SharedKernel/             # Общие примитивы, нормализация строк, валидация имен ФС, Result<T>, TimeProvider
│   ├── Modules/
│   │   ├── TypoErp.Modules.Identity/     # Пользователи, роли, гранулярные права (Permissions)
│   │   ├── TypoErp.Modules.Products/     # Модуль Товаров (Core, Storage, Sync, Auto-Normalization, Postgres EF, LockService)
│   │   ├── TypoErp.Modules.Assembly/     # Модуль Сборки (этап 2)
│   │   └── TypoErp.Modules.Marketplaces/ # Модуль Маркетплейсов (этап 3)
│   └── TypoErp.Web/                      # Хост-приложение Blazor Server (MudBlazor UI, DI, Cookie Auth, Workspace Lock Session)
└── tests/
    ├── TypoErp.SharedKernel.Tests/
    ├── TypoErp.Modules.Identity.Tests/
    ├── TypoErp.Modules.Products.Tests/   # Unit и Integration тесты модуля товаров
    └── TypoErp.Web.Tests/                # Тесты авторизационных Claims и сессионного координатора блокировок Blazor Circuit