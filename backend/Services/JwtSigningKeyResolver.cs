namespace VisionPaint.Services;

public static class JwtSigningKeyResolver
{
    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? Environment.GetEnvironmentVariable("VISIONPAINT_JWT_SIGNING_KEY");

        if (string.IsNullOrWhiteSpace(signingKey) && environment.IsDevelopment())
        {
            signingKey = "local-dev-only-change-for-production-32chars";
        }

        if (string.IsNullOrWhiteSpace(signingKey) && environment.IsEnvironment("Testing"))
        {
            signingKey = "visionpaint-test-signing-key-32chars-min!!";
        }

        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Set Jwt:SigningKey or VISIONPAINT_JWT_SIGNING_KEY (minimum 32 characters).");
        }

        return signingKey;
    }
}
