using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// If you want to avoid the https redirect warning for local HTTP-only runs, comment this out:
// app.UseHttpsRedirection();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/api/ping", () => Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow }));

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
