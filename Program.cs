using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using StanTrack.BackgroundJobs;
using StanTrack.Data;
using StanTrack.ExternalApis;
using StanTrack.Interfaces;
using StanTrack.Models;
using StanTrack.Repositories;
using StanTrack.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddHttpClient<TicketmasterClient>(c =>
    c.BaseAddress = new Uri("https://app.ticketmaster.com/discovery/v2/"));
builder.Services.AddHttpClient<TmdbClient>(c =>
    c.BaseAddress = new Uri("https://api.themoviedb.org/3/"));
builder.Services.AddHttpClient<MusicBrainzClient>(c =>
{
    c.BaseAddress = new Uri("https://musicbrainz.org/ws/2/");
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
