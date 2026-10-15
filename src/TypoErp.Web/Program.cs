using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Serilog;
using TypoErp.Modules.Identity.Management;
using TypoErp.Modules.Identity.Persistence;
using TypoErp.Modules.Identity.Security;
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
using TypoErp.Web.Workspace;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine("logs", "typoerp-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14
    )
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

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
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton(TimeProvider.System);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured."
    );

// Модуль Identity
builder.Services.Configure<DefaultAdminOptions>(
    builder.Configuration.GetSection(DefaultAdminOptions.SectionName)
);
builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();

// Модуль Products
builder.Services.Configure<ProductsStorageOptions>(
    builder.Configuration.GetSection(ProductsStorageOptions.SectionName)
);
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IOptions<ProductsStorageOptions>>().Value
);

builder.Services.AddDbContext<ProductsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<IProductStorage, LocalDiskProductStorage>();
builder.Services.AddSingleton<IProductsWorkspaceLockService, ProductsWorkspaceLockService>();
builder.Services.AddScoped<IProductsWorkspaceCircuitSession, ProductsWorkspaceCircuitSession>();
builder.Services.AddScoped<IProductSyncService, ProductSyncService>();
builder.Services.AddScoped<IProductManagementService, ProductManagementService>();

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
