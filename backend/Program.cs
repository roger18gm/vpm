using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using VisionPaint.Data;

var builder = WebApplication.CreateBuilder(args);

// Load local settings if they exist
var localSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.local.json");
if (File.Exists(localSettingsPath))
{
    builder.Configuration.AddJsonFile(localSettingsPath, optional: true, reloadOnChange: true);
}

// Add services to the container
builder.Services.AddControllers();

// Add Entity Framework Core and PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.")));

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000", "https://visionpainting.web.app")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseHttpsRedirection();
app.UseCors("AllowLocalhost");
app.UseAuthorization();
app.MapControllers();

// Add a simple health check endpoint
app.MapGet("/api/health", () => new { status = "ok", time = DateTime.UtcNow });

// Determine port: Azure uses 8080, local defaults to 5000
var port = Environment.GetEnvironmentVariable("ASPNETCORE_PORT")
    ?? (app.Environment.IsProduction() ? "8080" : "5000");

try
{
    // Listen on all interfaces (0.0.0.0) for Azure, localhost for local dev
    var bindAddress = app.Environment.IsProduction() ? "0.0.0.0" : "localhost";
    app.Run($"http://{bindAddress}:{port}");
}
catch (Exception ex)
{
    Console.WriteLine($"Fatal error: {ex}");
    throw;
}
