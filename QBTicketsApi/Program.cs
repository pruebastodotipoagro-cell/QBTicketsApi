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
        ),
        npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null
            );

            npgsqlOptions.CommandTimeout(30);
        }
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

builder.Services
    .AddHttpClient(string.Empty, client =>
    {
        client.Timeout =
            TimeSpan.FromSeconds(35);

        client.DefaultRequestVersion =
            System.Net.HttpVersion.Version11;

        client.DefaultVersionPolicy =
            System.Net.Http.HttpVersionPolicy.RequestVersionOrLower;
    })
    .ConfigurePrimaryHttpMessageHandler(
        () => new SocketsHttpHandler
        {
            ConnectTimeout =
                TimeSpan.FromSeconds(10),

            PooledConnectionLifetime =
                TimeSpan.FromMinutes(1),

            PooledConnectionIdleTimeout =
                TimeSpan.FromSeconds(15),

            ConnectCallback =
                async (context, cancellationToken) =>
                {
                    var addresses =
                        await System.Net.Dns
                            .GetHostAddressesAsync(
                                context.DnsEndPoint.Host,
                                cancellationToken
                            );

                    var ipv4 =
                        addresses.FirstOrDefault(
                            x =>
                                x.AddressFamily ==
                                System.Net.Sockets
                                    .AddressFamily.InterNetwork
                        );

                    if (ipv4 == null)
                    {
                        throw new Exception(
                            "No se encontró una dirección IPv4 para " +
                            context.DnsEndPoint.Host
                        );
                    }

                    var socket =
                        new System.Net.Sockets.Socket(
                            System.Net.Sockets.AddressFamily.InterNetwork,
                            System.Net.Sockets.SocketType.Stream,
                            System.Net.Sockets.ProtocolType.Tcp
                        );

                    try
                    {
                        await socket.ConnectAsync(
                            new System.Net.IPEndPoint(
                                ipv4,
                                context.DnsEndPoint.Port
                            ),
                            cancellationToken
                        );

                        return new System.Net.Sockets.NetworkStream(
                            socket,
                            ownsSocket: true
                        );
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
        }
    );
builder.Services.AddMemoryCache();

builder.Services.AddScoped<QuickBooksService>();

// builder.Services.AddHostedService<
//     QuickBooksTokenRefreshWorker
// );

QuestPDF.Settings.License =
    LicenseType.Community;

builder.Services.AddScoped<TicketPdfService>();
builder.Services.AddScoped<FelService>();
builder.Services.AddSingleton<CustomerLookupService>();
builder.Services.AddScoped<MegaprintService>();
builder.Services.AddScoped<FelXmlBuilderService>();
builder.Services.AddScoped<ReportsService>();
builder.Services.AddScoped<CashMovementService>();
builder.Services
    .AddScoped<
        FelCancellationXmlBuilderService
    >();
builder.Services
    .AddScoped<
        FelCancellationService
    >();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
