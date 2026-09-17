using System.Text.Json.Serialization;
using Claims.Api;
using Claims.Application;
using Claims.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(allowIntegerValues: false)))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors
                .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                    ? "Invalid value." : error.ErrorMessage).ToArray());
        return new BadRequestObjectResult(ApiProblems.Validation(context.HttpContext, errors))
        {
            ContentTypes = { "application/problem+json" }
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ClaimService>();
builder.Services.AddScoped<IClaimRepository, ClaimRepository>();
builder.Services.AddDbContext<ClaimsDbContext>(o => o.UseSqlServer(
    builder.Configuration.GetConnectionString("Claims")
        ?? throw new InvalidOperationException("ConnectionStrings:Claims is required.")));
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    if (app.Configuration.GetValue<bool>("Database:Initialize"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        await db.Database.MigrateAsync();
        await DevelopmentSeeder.Seed(db);
    }
}
app.MapControllers();
app.Run();
public partial class Program;

