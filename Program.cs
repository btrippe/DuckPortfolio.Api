using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

var authority = builder.Configuration["Entra:Authority"];
var tenantId = builder.Configuration["Entra:TenantId"];
var audience = builder.Configuration["Entra:Audience"];
var additionalAudiences = BuildValidAudiences(audience);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = string.IsNullOrWhiteSpace(authority)
            ? $"https://login.microsoftonline.com/{tenantId}/v2.0"
            : authority;
        options.Audience = audience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidAudiences = additionalAudiences,
            NameClaimType = "name",
            RoleClaimType = "roles"
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    return new SqlConnection(connectionString);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

// If you want to avoid the https redirect warning for local HTTP-only runs, comment this out:
// app.UseHttpsRedirection();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/api/ping", () => Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow }));
app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    var objectId = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    var subject = user.FindFirst("sub")?.Value ?? objectId;
    var email = user.FindFirst("preferred_username")?.Value
        ?? user.FindFirst("emails")?.Value
        ?? user.FindFirst(ClaimTypes.Email)?.Value;

    var claims = user.Claims
        .Select(claim => new
        {
            claim.Type,
            claim.Value
        })
        .ToArray();

    return Results.Ok(new
    {
        isAuthenticated = user.Identity?.IsAuthenticated ?? false,
        name = user.Identity?.Name,
        subject,
        objectId,
        email,
        claims
    });
})
.RequireAuthorization();
app.MapGet("/api/admin/ping", (ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        message = "Admin access confirmed.",
        name = user.Identity?.Name
    });
})
.RequireAuthorization(policy => policy.RequireRole("Admin"));
app.MapGet("/api/database/ping", async (SqlConnection connection, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(connection.ConnectionString))
    {
        return Results.Problem(
            title: "Database connection string is not configured.",
            detail: "Set ConnectionStrings:DefaultConnection in appsettings.Development.json or use the ConnectionStrings__DefaultConnection environment variable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                @@SERVERNAME AS ServerName,
                DB_NAME() AS DatabaseName,
                SUSER_SNAME() AS LoginName,
                SYSDATETIMEOFFSET() AS CheckedAt
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return Results.Ok(new
        {
            ok = true,
            server = reader.GetString(0),
            database = reader.GetString(1),
            login = reader.GetString(2),
            checkedAt = reader.GetDateTimeOffset(3)
        });
    }
    catch (SqlException ex)
    {
        return Results.Problem(
            title: "Database connection failed.",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

static string[] BuildValidAudiences(string? configuredAudience)
{
    if (string.IsNullOrWhiteSpace(configuredAudience))
    {
        return Array.Empty<string>();
    }

    var audiences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        configuredAudience
    };

    const string apiPrefix = "api://";
    if (configuredAudience.StartsWith(apiPrefix, StringComparison.OrdinalIgnoreCase))
    {
        audiences.Add(configuredAudience[apiPrefix.Length..]);
    }

    return audiences.ToArray();
}
