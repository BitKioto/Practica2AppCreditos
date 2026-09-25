using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Practica2AppCreditos.Data;
using Practica2AppCreditos.Hubs;
using Practica2AppCreditos.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
if (!Directory.Exists(dataDir))
{
    Directory.CreateDirectory(dataDir);
}

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var useRedisCache = !string.IsNullOrWhiteSpace(redisConnectionString);

if (useRedisCache && builder.Environment.IsDevelopment())
{
    try
    {
        var probeOptions = ConfigurationOptions.Parse(redisConnectionString!);
        probeOptions.AbortOnConnectFail = true;
        probeOptions.ConnectRetry = 0;
        probeOptions.ConnectTimeout = 2000;

        await using var redisProbe =
            await ConnectionMultiplexer.ConnectAsync(probeOptions);
    }
    catch (Exception exception) when (
        exception is RedisException or TimeoutException or ArgumentException or FormatException)
    {
        useRedisCache = false;
    }
}

if (useRedisCache)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "Practica2AppCreditos:";
    });
}
else
{
    // Permite ejecutar y probar la aplicación localmente sin una instancia de Redis activa.
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddSingleton<RabbitMQProducer>();

var rabbitMqConsumerEnabled = true;
if (bool.TryParse(
        builder.Configuration["RabbitMq:ConsumerEnabled"],
        out bool configuredConsumerEnabled))
{
    rabbitMqConsumerEnabled = configuredConsumerEnabled;
}

if (rabbitMqConsumerEnabled)
{
    builder.Services.AddHostedService<SolicitudConsumerService>();
}

builder.Services.ConfigureApplicationCookie(options =>
{
    options.AccessDeniedPath = "/Account/AccessDenied";

    // Un usuario no autenticado que intente abrir el panel recibe la misma
    // pantalla de acceso denegado que un usuario autenticado sin el rol.
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments(
                "/Analista",
                StringComparison.OrdinalIgnoreCase))
        {
            var returnUrl = context.Request.Path.ToString() +
                            context.Request.QueryString.ToString();
            var accessDeniedUrl =
                $"{context.Request.PathBase}{options.AccessDeniedPath}?returnUrl={Uri.EscapeDataString(returnUrl)}";

            context.Response.Redirect(accessDeniedUrl);
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

var app = builder.Build();

if (!useRedisCache && !string.IsNullOrWhiteSpace(redisConnectionString))
{
    app.Logger.LogWarning(
        "Redis no está disponible; se usará caché en memoria para el entorno local.");
}

await DbInitializer.InitializeAsync(app.Services);

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

app.UseHttpsRedirection();
app.UseWebSockets();
app.UseSession();
app.UseRouting();
app.UseAuthentication();

app.UseAuthorization();

app.MapHub<SolicitudesHub>("/hubs/solicitudes");
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
