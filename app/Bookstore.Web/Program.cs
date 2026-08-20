using System.IO;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Amazon.Rekognition;
using Amazon.S3;
using BobsBookstoreClassic.Data;
using Bookstore.Data;
using Bookstore.Data.FileServices;
using Bookstore.Data.ImageResizeService;
using Bookstore.Data.ImageValidationServices;
using Bookstore.Data.Repositories;
using Bookstore.Domain;
using Bookstore.Domain.Addresses;
using Bookstore.Domain.Books;
using Bookstore.Domain.Carts;
using Bookstore.Domain.Customers;
using Bookstore.Domain.Offers;
using Bookstore.Domain.Orders;
using Bookstore.Domain.ReferenceData;
using Bookstore.Web;
using Bookstore.Web.Helpers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;

// -----------------------------------------------------------------------
// Bootstrap – initialise BookstoreConfiguration from appsettings.json
// -----------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// Seed the legacy static BookstoreConfiguration from IConfiguration so
// all BookstoreConfiguration.GetSetting() calls continue to work.
BookstoreConfiguration.Initialize(builder.Configuration);

// Optionally pull additional settings from AWS Systems Manager
ConfigurationSetup.ConfigureConfiguration();

// -----------------------------------------------------------------------
// Logging – NLog
// -----------------------------------------------------------------------
LoggingSetup.ConfigureLogging();
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// -----------------------------------------------------------------------
// Autofac – use Autofac as the DI container
// -----------------------------------------------------------------------
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// -----------------------------------------------------------------------
// MVC with Areas
// -----------------------------------------------------------------------
builder.Services.AddControllersWithViews();

// -----------------------------------------------------------------------
// EF Core
// -----------------------------------------------------------------------
var connectionString = BookstoreConfiguration.GetConnectionString("BookstoreDatabaseConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// -----------------------------------------------------------------------
// Authentication
// -----------------------------------------------------------------------
AuthenticationSetup.ConfigureAuthentication(builder.Services, builder.Configuration);

// -----------------------------------------------------------------------
// Autofac container registrations
// -----------------------------------------------------------------------
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    // Domain services
    containerBuilder.RegisterType<BookService>().As<IBookService>();
    containerBuilder.RegisterType<OrderService>().As<IOrderService>();
    containerBuilder.RegisterType<ReferenceDataService>().As<IReferenceDataService>();
    containerBuilder.RegisterType<OfferService>().As<IOfferService>();
    containerBuilder.RegisterType<CustomerService>().As<ICustomerService>();
    containerBuilder.RegisterType<AddressService>().As<IAddressService>();
    containerBuilder.RegisterType<ShoppingCartService>().As<IShoppingCartService>();
    containerBuilder.RegisterType<ImageResizeService>().As<IImageResizeService>();

    // Repositories
    containerBuilder.RegisterType<CustomerRepository>().As<ICustomerRepository>();
    containerBuilder.RegisterType<AddressRepository>().As<IAddressRepository>();
    containerBuilder.RegisterType<BookRepository>().As<IBookRepository>();
    containerBuilder.RegisterType<OfferRepository>().As<IOfferRepository>();
    containerBuilder.RegisterType<ShoppingCartRepository>().As<IShoppingCartRepository>();
    containerBuilder.RegisterType<OrderRepository>().As<IOrderRepository>();
    containerBuilder.RegisterType<ReferenceDataRepository>().As<IReferenceDataRepository>();

    // Paginated list
    containerBuilder.RegisterGeneric(typeof(PaginatedList<>))
        .As(typeof(IPaginatedList<>))
        .InstancePerLifetimeScope();

    // File service
    if (BookstoreConfiguration.GetSetting("Services/FileService") == "aws")
    {
        containerBuilder.RegisterType<AmazonS3Client>().As<IAmazonS3>();
        containerBuilder.RegisterType<S3FileService>().As<IFileService>();
    }
    else
    {
        containerBuilder.Register(c =>
        {
            var env = c.Resolve<IWebHostEnvironment>();
            var contentPath = Path.Combine(env.ContentRootPath, "Content");
            return new LocalFileService(contentPath);
        }).As<IFileService>().SingleInstance();
    }

    // Image validation service
    if (BookstoreConfiguration.GetSetting("Services/ImageValidationService") == "aws")
    {
        containerBuilder.RegisterType<AmazonRekognitionClient>().As<IAmazonRekognition>();
        containerBuilder.RegisterType<RekognitionImageValidationService>().As<IImageValidationService>();
    }
    else
    {
        containerBuilder.RegisterType<LocalImageValidationService>().As<IImageValidationService>();
    }

    // Local auth middleware (only needed when not using AWS Cognito)
    if (BookstoreConfiguration.GetSetting("Services/Authentication") != "aws")
    {
        containerBuilder.RegisterType<LocalAuthenticationMiddleware>();
    }
});

// -----------------------------------------------------------------------
// Build
// -----------------------------------------------------------------------
var app = builder.Build();

// -----------------------------------------------------------------------
// Seed the database
// -----------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.EnsureCreated();
    BookstoreDbInitializer.Seed(context);
}

// -----------------------------------------------------------------------
// Middleware pipeline
// -----------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// Serve static files from Content/ and Scripts/ folders (legacy paths)
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "Content")),
    RequestPath = "/Content"
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "Scripts")),
    RequestPath = "/Scripts"
});

app.UseRouting();

// Add local auth middleware before UseAuthentication when not using AWS
if (BookstoreConfiguration.GetSetting("Services/Authentication") != "aws")
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

app.UseAuthentication();
app.UseAuthorization();

// -----------------------------------------------------------------------
// Routes
// -----------------------------------------------------------------------
app.MapControllerRoute(
    name: "Admin_default",
    pattern: "Admin/{controller}/{action}/{id?}",
    defaults: new { area = "Admin", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
