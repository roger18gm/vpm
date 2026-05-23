using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VisionPaint.Data;
using VisionPaint.Models;
using VisionPaint.Services;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Logging.ClearProviders();
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "VisionPaint", "DataProtectionKeys")))
        .SetApplicationName("VisionPaint.Tests");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IPasswordHasher<AuthUser>, PasswordHasher<AuthUser>>();

var signingKey = JwtSigningKeyResolver.Resolve(builder.Configuration, builder.Environment);

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Configure(options => options.SigningKey = signingKey);

builder.Services.AddSingleton<ITokenService, TokenService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var issuer = string.IsNullOrWhiteSpace(jwtOptions.Issuer) ? "VisionPaint" : jwtOptions.Issuer;
var audience = string.IsNullOrWhiteSpace(jwtOptions.Audience) ? "VisionPaint" : jwtOptions.Audience;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var tokenType = context.Principal?.FindFirst("token_type")?.Value;
                if (string.Equals(tokenType, TokenService.RefreshTokenType, StringComparison.Ordinal))
                {
                    context.Fail("Refresh tokens cannot be used for API access.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var envCorsOrigins = Environment.GetEnvironmentVariable("VISIONPAINT_CORS_ORIGINS")
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var configCorsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();

var corsOrigins = envCorsOrigins is { Length: > 0 }
    ? envCorsOrigins
    : configCorsOrigins is { Length: > 0 }
        ? configCorsOrigins
        : new[]
        {
            "http://localhost:3000",
            "http://localhost:5173",
            "http://127.0.0.1:3000",
            "http://127.0.0.1:5173"
        };

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
