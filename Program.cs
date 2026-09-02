using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using StanTrack.BackgroundJobs;
using StanTrack.Data;
using StanTrack.ExternalApis;
using StanTrack.Interfaces;
using StanTrack.Models;
using StanTrack.Repositories;
using StanTrack.Services;

var builder = WebApplication.CreateBuilder(args);

// Pin culture to invariant so date formatting is always English regardless of server locale.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// AddDbContextFactory alone registers both the singleton IDbContextFactory and the scoped
// ApplicationDbContext (via the factory). Registering AddDbContext separately conflicts:
// the singleton factory cannot consume the scoped DbContextOptions that plain AddDbContext adds.
// EventSyncService uses the factory directly to give each parallel insert worker its own context.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.AddOpenApi();

// Lock request culture — prevents Accept-Language from shifting date formatting to sv-SE etc.
builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en-US");
    options.SupportedCultures = new[] { new System.Globalization.CultureInfo("en-US") };
    options.SupportedUICultures = new[] { new System.Globalization.CultureInfo("en-US") };
});

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddHttpClient<TicketmasterClient>(c =>
{
    c.BaseAddress = new Uri("https://app.ticketmaster.com/discovery/v2/");
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<TmdbClient>(c =>
{
    c.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<MusicBrainzClient>(c =>
{
    c.BaseAddress = new Uri("https://musicbrainz.org/ws/2/");
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("StanTrack/1.0 (https://github.com/lexutb/StanTrack)");
});
builder.Services.AddScoped<IEnumerable<IEventFetchService>>(sp => new IEventFetchService[]
{
    sp.GetRequiredService<TicketmasterClient>(),
    sp.GetRequiredService<TmdbClient>(),
    sp.GetRequiredService<MusicBrainzClient>(),
});

builder.Services.AddScoped<EventSyncService>();
builder.Services.AddHostedService<StanTrackSyncBackgroundService>();

builder.Services.AddTransient<IEmailSender, BrevoEmailSender>();
builder.Services.AddTransient<IEmailSender<ApplicationUser>, BrevoEmailSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.MapOpenApi();
    app.MapScalarApiReference();    
}

using (var scope = app.Services.CreateScope())
{
    await SeedData.InitializeAsync(scope.ServiceProvider);
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
