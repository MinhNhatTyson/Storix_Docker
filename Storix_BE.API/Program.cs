using CarServ.API.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Storix_BE.API.Configuration;
using Storix_BE.Domain.Context;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<StorixDbContext>(options =>
    options.UseNpgsql(connectionString));
var config = builder.Configuration;
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(config)
    .CreateLogger();

builder.Services.AddControllers();
builder.Logging.AddSerilog();
builder.Services.AddSingleton(Log.Logger);
builder.Services.AddSingleton<Serilog.Extensions.Hosting.DiagnosticContext>();
builder.Services.AddAutoMapper(typeof(Program));
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
});

/*builder.Services.AddDatabaseConfiguration(config);*/
builder.Services.AddServiceConfiguration(config);
builder.Services.AddRepositoryConfiguration(config);
builder.Services.AddJwtAuthenticationService(config);
builder.Services.AddThirdPartyServices(config);
builder.Services.AddSwaggerService();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.AddCors(opt =>
{
   opt.AddPolicy("CorsPolicy", policy =>
    {
        //Set cors to accept Vite dev server
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithOrigins("https://localhost:5173");
    });
});
builder.Services.AddEndpointsApiExplorer();


var app = builder.Build();
app.UseSerilogRequestLogging();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("CorsPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
