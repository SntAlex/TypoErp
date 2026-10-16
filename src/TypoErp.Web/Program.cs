using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;
using TypoErp.Modules.Identity.Management;
using TypoErp.Modules.Identity.Persistence;
using TypoErp.Modules.Identity.Security;
using TypoErp.Modules.Products.Catalog;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Locking;
using TypoErp.Modules.Products.Management;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Modules.Products.Storage;
using TypoErp.Modules.Products.Sync;
using TypoErp.Web.Components;
using TypoErp.Web.Configuration;
using TypoErp.Web.Endpoints;
using TypoErp.Web.Infrastructure;
using TypoErp.Web.Security;
using TypoErp.Web.Workspace;

var builder = WebApplication.CreateBuilder(args);

var logsDirectory = Path.Combine(builder.Environment.ContentRootPath, "logs");
Directory.CreateDirectory(logsDirectory);

builder.Host.UseSerilog(
    (context, services, loggerConfiguration) =>
    {
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logsDirectory, "typoerp-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14
            );
    }
);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);

builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TypoErp.Auth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured."
    );

builder.Services.AddDbContext<IdentityDbContext>(
    options =>
        options.UseNpgsql(
            connectionString,
            npgsqlOptions =>
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
        ),
    contextLifetime: ServiceLifetime.Transient,
    optionsLifetime: ServiceLifetime.Singleton
);

builder.Services.AddDbContext<ProductsDbContext>(
    options =>
        options.UseNpgsql(
            connectionString,
            npgsqlOptions =>
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
        ),
    contextLifetime: ServiceLifetime.Transient,
    optionsLifetime: ServiceLifetime.Singleton
);

var storageOptions =
    builder
        .Configuration.GetSection(ProductsStorageOptions.SectionName)
        .Get<ProductsStorageOptions>()
    ?? new ProductsStorageOptions();

if (string.IsNullOrWhiteSpace(storageOptions.RootPath))
    storageOptions.RootPath = Path.Combine(
        builder.Environment.ContentRootPath,
        "storage",
        "products"
    );
else if (!Path.IsPathRooted(storageOptions.RootPath))
    storageOptions.RootPath = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, storageOptions.RootPath)
    );

Directory.CreateDirectory(storageOptions.RootPath);

var defaultAdminOptions =
    builder.Configuration.GetSection(DefaultAdminOptions.SectionName).Get<DefaultAdminOptions>()
    ?? new DefaultAdminOptions();

builder.Services.AddSingleton(storageOptions);
builder.Services.AddSingleton(defaultAdminOptions);

// Модуль Identity
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddTransient<IUserManagementService, UserManagementService>();

// Модуль Products
builder.Services.AddSingleton<IProductStorage, LocalDiskProductStorage>();
builder.Services.AddSingleton<IProductsWorkspaceLockService, ProductsWorkspaceLockService>();
builder.Services.AddTransient<IProductSyncService, ProductSyncService>();
builder.Services.AddTransient<IProductManagementService, ProductManagementService>();
builder.Services.AddTransient<IProductCatalogQueryService, ProductCatalogQueryService>();

// Координатор блокировок сессии Blazor Circuit (сохраняет SessionId из Cookie при нажатии F5)
builder.Services.AddScoped<IProductsWorkspaceCircuitSession, ProductsWorkspaceCircuitSession>();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAuthEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
