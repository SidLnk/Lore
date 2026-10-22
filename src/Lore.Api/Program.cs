using System.Threading.RateLimiting;
using Lore.Api;
using Lore.Api.Data;
using Lore.Api.Endpoints;
using Lore.Api.Media;
using Lore.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var volumes = config.GetSection("Storage:Volumes").GetChildren()
    .Select(v => (Id: v["Id"]!, Name: v["Name"] ?? v["Id"]!, Root: v["Root"]!))
    .ToList();
if (volumes.Count == 0) throw new InvalidOperationException("Configure at least one Storage:Volumes entry");

var db = new Db(config["Lore:DbPath"]!);
var family = (config["Lore:Family"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
if (family.Length == 0) throw new InvalidOperationException("Set Lore:Family to the family members' names, comma separated");
db.Migrate(volumes.Select(v => (v.Id, v.Name)), family);
var paths = new LorePaths(config["Lore:DataRoot"]!);

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(new StorageRegistry(volumes.Select(IStorageVolume (v) => new LocalStorageVolume(v.Id, v.Root)).ToList()));
builder.Services.AddSingleton<Ffmpeg>();
builder.Services.AddSingleton(sp => new Thumbnails(paths.Derived, sp.GetRequiredService<StorageRegistry>(),
    sp.GetRequiredService<Ffmpeg>(), sp.GetRequiredService<ILogger<Thumbnails>>()));
builder.Services.AddSingleton<IIngestStep, MetadataIngestStep>();
builder.Services.AddSingleton<MaintenanceService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MaintenanceService>());

builder.Services.AddAuthentication(DeviceAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DeviceAuthHandler>(DeviceAuthHandler.SchemeName, null)
    .AddScheme<AuthenticationSchemeOptions, AdminAuthHandler>(AdminAuthHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("device", p => p.AddAuthenticationSchemes(DeviceAuthHandler.SchemeName).RequireAuthenticatedUser())
    .AddPolicy("admin", p => p.AddAuthenticationSchemes(AdminAuthHandler.SchemeName).RequireAuthenticatedUser());

// 6-digit codes: cap guesses so the 10-minute window can't be brute-forced.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("pair", w => { w.PermitLimit = 10; w.Window = TimeSpan.FromMinutes(1); });
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api").RequireAuthorization("device");
DeviceEndpoints.Map(api);
UploadEndpoints.Map(api);
MediaEndpoints.Map(api);
OrganizeEndpoints.Map(api);

AdminEndpoints.Map(app.MapGroup("/admin").RequireAuthorization("admin"), app.Environment);

app.Run();

public partial class Program;
