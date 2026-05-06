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
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
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

var port = 5000;
app.Run($"http://localhost:{port}");
