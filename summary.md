# Migration Summary: .NET Framework 4.8 → .NET 10

## Status
**Build result: SUCCEEDED — 0 errors, 0 compiler warnings**

---

## What was migrated

### Project files
| Project | Before | After |
|---------|--------|-------|
| Bookstore.Web | Old-style .csproj (TargetFrameworkVersion v4.8, non-SDK) | SDK-style `Microsoft.NET.Sdk.Web`, `net10.0` |
| Bookstore.Data | SDK-style `netstandard2.0` | SDK-style `net10.0` |
| Bookstore.Domain | SDK-style `netstandard2.0` | SDK-style `net10.0` |
| Bookstore.Common | SDK-style `netstandard2.0` | SDK-style `net10.0` |
| Bookstore.Cdk | Already `net10.0` | Unchanged |

### Framework / hosting
- **Global.asax + OWIN Startup.cs** → **Program.cs** (ASP.NET Core minimal hosting model)
- Removed `OwinStartup` attribute, `IAppBuilder`, all `Microsoft.Owin.*` references
- Created `appsettings.json` from `Web.config` settings

### Authentication
- **Microsoft.Owin.Security.OpenIdConnect / Cookies** → **Microsoft.AspNetCore.Authentication.OpenIdConnect + AddCookie**
- `AuthenticationSetup.ConfigureAuthentication(IServiceCollection, IConfiguration)` — supports both local and AWS Cognito modes
- `LocalAuthenticationMiddleware` rewritten as `IMiddleware` (ASP.NET Core)

### Dependency Injection
- **Autofac.Integration.Mvc / Autofac.Integration.Owin** → **Autofac.Extensions.DependencyInjection** (`UseServiceProviderFactory(new AutofacServiceProviderFactory())`)
- All service/repository registrations moved to `Program.cs` via `ConfigureContainer<ContainerBuilder>`
- `LocalFileService` injected with `IWebHostEnvironment.ContentRootPath`

### Data access: EF6 → EF Core 10
- `ApplicationDbContext` constructor changed from `(string connectionString)` to `(DbContextOptions<ApplicationDbContext> options)`
- `OnModelCreating(DbModelBuilder)` → `OnModelCreating(ModelBuilder)` with EF Core fluent API
- EF6-specific methods replaced:
  - `HasRequired/WithMany/WillCascadeOnDelete` → `HasOne/WithMany/OnDelete(DeleteBehavior.NoAction)`
  - `HasDatabaseGeneratedOption(Identity)` → `ValueGeneratedOnAdd()`
  - `modelBuilder.Conventions.Remove<PluralizingTableNameConvention>()` — removed (EF Core default is non-pluralizing)
- `Database.SetInitializer` removed; replaced with `BookstoreDbInitializer.Seed(context)` called from `Program.cs` after `context.Database.EnsureCreated()`
- All repositories: `using System.Data.Entity` → `using Microsoft.EntityFrameworkCore`
- EF6 string-based `Include("Genre")` → lambda `.Include(x => x.Genre)`
- EF6 nested includes `Include(x => x.OrderItems.Select(y => y.Book))` → `.Include(x => x.OrderItems).ThenInclude(y => y.Book)`
- Synchronous `await Task.Run(() => dbContext.X.Add(y))` patterns → `dbContext.X.Add(y); return Task.CompletedTask`
- `PaginatedList.cs`: `System.Data.Entity` → `Microsoft.EntityFrameworkCore`

### Configuration
- `BookstoreConfiguration` (in `BobsBookstoreClassic.Data`) rewritten to:
  - Remove `System.Configuration.ConfigurationManager`
  - Add `static Initialize(IConfiguration config)` method called from `Program.cs`
  - Preserve all existing `GetSetting` / `AddSetting` / `GetConnectionString` static APIs
- `appsettings.json` created with all settings from `Web.config`

### Logging
- **NLog** retained; wired into ASP.NET Core via `NLog.Web.AspNetCore` + `builder.Host.UseNLog()`
- `LoggingSetup.ConfigureLogging()` preserved, called during startup before the host is built

### Controllers & Views
- All controllers: `using System.Web.Mvc` → `using Microsoft.AspNetCore.Mvc`, `ActionResult` → `IActionResult`
- `AuthenticationController`: `HttpCookie` → `Response.Cookies.Delete/Append`, `Request.Url.*` → `Request.Scheme/Host`
- `InventoryController`: `model.CoverImage?.InputStream` → `model.CoverImage?.OpenReadStream()`
- `AdminAreaRegistration` stubbed out (area routing via `[Area("Admin")]` on `AdminAreaControllerBase`)
- `Html.EnumDropDownListFor` replaced with `<select asp-for asp-items>` tag helper (Orders/Index, Offers/Index)

### Models
- `HttpPostedFileBase CoverImage` → `IFormFile CoverImage` in `InventoryCreateUpdateViewModel`
- `MaxFileSizeAttribute` / `ImageTypesAttribute`: `HttpPostedFileBase` → `IFormFile`
- All `using System.Web.Mvc` for `SelectListItem` → `using Microsoft.AspNetCore.Mvc.Rendering`

### Static files
- Configured `UseStaticFiles` with `PhysicalFileProvider` for `Content/` and `Scripts/` folders (legacy paths preserved)
- No files moved; existing folder structure retained

### View imports
- `Views/_ViewImports.cshtml` updated with `Microsoft.AspNetCore.Mvc.Rendering` and `Microsoft.AspNetCore.Routing`
- Created `Areas/Admin/Views/_ViewImports.cshtml` with same imports

### Package upgrades
- `Magick.NET-Q8-AnyCPU`: 14.6.0 → 14.16.0 (cleared ~140 NU190x vulnerability warnings)

---

## Remaining warnings
| Warning | Source | Note |
|---------|--------|------|
| NU1901 (2×) | `Amazon.CDK.Lib 2.188.0` in `Bookstore.Cdk` | Pre-existing low-severity vulnerability in CDK infrastructure project. Upgrading to 2.266.0 resolves the NU1901 but introduces CS0612/CS0618 obsolete-API warnings in CDK code outside migration scope. |

---

## Next steps / Notes
- **Database migrations**: EF Core's `EnsureCreated()` is used for initial database creation. For production upgrades of existing databases, run `dotnet ef migrations add InitialMigration` and `dotnet ef database update` once connectivity is available.
- **SQL Server connection string**: The `appsettings.json` default uses `(localdb)\MSSQLLocalDB`. Update for production environments.
- **HTTPS / Cognito redirect URIs**: ASP.NET Core OIDC `signin-oidc` callback is used. Ensure Cognito app client has `https://{host}/signin-oidc` registered as an allowed callback URL.
- **CDK ECS stack**: The `EcsStack.cs` may need the Docker image reference updated; the `Dockerfile` in the repo still targets the old framework and should be updated for the `net10.0` SDK image.
- **`Amazon.CDK.Lib` upgrade**: Upgrading to ≥ 2.266.0 fixes the NU1901 vulnerability but requires updating `CloudFrontWebDistribution` → `Distribution` in `CoreStack.cs` (pre-existing obsolete CDK API usage).
