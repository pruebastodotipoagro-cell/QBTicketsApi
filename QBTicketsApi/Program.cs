using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QBTicketsApi.Database;
using QBTicketsApi.Services;
using QuestPDF.Infrastructure;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString(
            "DefaultConnection"
        )
    )
);

string jwtKey =
    builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "No está configurada la variable Jwt:Key."
    );

string jwtIssuer =
    builder.Configuration["Jwt:Issuer"]
    ?? "QBTicketsApi";

string jwtAudience =
    builder.Configuration["Jwt:Audience"]
    ?? "QBTicketsNative";

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme
    )
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)
                    ),

                ClockSkew = TimeSpan.FromMinutes(1)
            };
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Services.AddScoped<QuickBooksService>();
builder.Services.AddHostedService<
    QuickBooksTokenRefreshWorker
>();

QuestPDF.Settings.License =
    LicenseType.Community;

builder.Services.AddScoped<TicketPdfService>();
builder.Services.AddScoped<FelService>();
builder.Services.AddSingleton<CustomerLookupService>();
builder.Services.AddScoped<MegaprintService>();
builder.Services.AddScoped<FelXmlBuilderService>();
builder.Services.AddScoped<ReportsService>();
builder.Services.AddScoped<CashMovementService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

/*
 * El orden es importante:
 * primero Authentication y después Authorization.
 */
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();