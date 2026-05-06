using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using TournamentManager.Infrastructure.Extensions;
using TournamentManager.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddTournamentManager(builder.Configuration);

var app = builder.Build();

// Автоматически применяем миграции при старте — нужно для docker compose up на чистой БД
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<TournamentDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
