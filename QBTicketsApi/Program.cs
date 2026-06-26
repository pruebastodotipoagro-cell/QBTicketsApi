using Microsoft.EntityFrameworkCore;
using QBTicketsApi.Database;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddScoped<QBTicketsApi.Services.QuickBooksService>();
builder.Services.AddHostedService<QBTicketsApi.Services.QuickBooksTokenRefreshWorker>();
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.AddScoped<QBTicketsApi.Services.TicketPdfService>();
builder.Services.AddScoped<QBTicketsApi.Services.FelService>();
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();