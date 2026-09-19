using FixPal.Data;
using FixPal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using FixPal.Features.Dalil;
using Azure.Identity;
using Azure.Storage.Blobs;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<FixPal.Services.SchedulingTimePolicy>();
builder.Services.AddScoped<FixPal.Services.ProviderCalendarService>();
builder.Services.AddScoped<FixPal.Services.ProviderBlackoutService>();
builder.Services.AddScoped<FixPal.Services.AppointmentService>();
builder.Services.AddScoped<FixPal.Services.AccountPhoneService>();
builder.Services.AddScoped<FixPal.Services.RequestCommunicationPolicy>();
builder.Services.AddScoped<FixPal.Services.RequestAccessService>();
builder.Services.AddScoped<FixPal.Services.RequestEvidencePolicy>();
builder.Services.AddScoped<FixPal.Services.RequestMutationService>();
builder.Services.AddScoped<FixPal.Services.RequestAgreementPolicy>();
builder.Services.AddScoped<FixPal.Services.RequestWorkflowService>();
builder.Services.AddScoped<FixPal.Services.RequestDetailsService>();
builder.Services.AddScoped<FixPal.Services.RequestCommerceService>();
builder.Services.AddScoped<FixPal.Services.ProviderMatchingService>();
if (builder.Environment.IsProduction())
{
    builder.Services.AddOptions<FixPal.Services.BlobStorageOptions>()
        .Bind(builder.Configuration.GetSection(FixPal.Services.BlobStorageOptions.SectionName))
        .Validate(options => options.TryGetBlobServiceUri(out _),
            "Storage:BlobServiceUri must be an absolute HTTPS Azure Blob service URI.")
        .ValidateOnStart();
    builder.Services.AddSingleton(sp =>
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FixPal.Services.BlobStorageOptions>>().Value;
        return new BlobServiceClient(options.GetBlobServiceUri(), new DefaultAzureCredential());
    });
    builder.Services.AddScoped<FixPal.Services.IPrivateMediaStorage, FixPal.Services.AzureBlobPrivateMediaStorage>();
    builder.Services.AddScoped<FixPal.Services.IPortfolioMediaStorage, FixPal.Services.AzureBlobPortfolioMediaStorage>();
}
else
{
    builder.Services.AddScoped<FixPal.Services.IPrivateMediaStorage, FixPal.Services.LocalPrivateMediaStorage>();
    builder.Services.AddScoped<FixPal.Services.IPortfolioMediaStorage, FixPal.Services.PortfolioMediaStorage>();
}

builder.Services.Configure<FixPal.Services.DiagnosisOptions>(builder.Configuration.GetSection("Diagnosis"));
builder.Services.AddHttpClient<FixPal.Services.IProblemDiagnosisService, FixPal.Services.OpenAiProblemDiagnosisService>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddDalilAssistant(builder.Configuration);
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("diagnosis", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("writes", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("identity", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsync("محاولات كثيرة. انتظر دقيقة ثم حاول مجددًا.", token);
    };
});

builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        // Hackathon MVP: no email provider yet, so confirmation stays disabled.
        // Re-enable it once a transactional email provider is configured.
        options.SignIn.RequireConfirmedAccount = false;

        options.User.RequireUniqueEmail = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

builder.Services.AddControllersWithViews(options =>
{
    // Protect unsafe MVC actions by default. Individual actions can opt out only
    // when there is a deliberate reason to do so.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

var app = builder.Build();

await IdentitySeeder.SeedRolesAndAdminAsync(app.Services);
await FixPalSeeder.SeedAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Small, dependency-free security baseline. A stricter CSP will be added after
// external assets (fonts/icons/map/AI) are finalized.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();

