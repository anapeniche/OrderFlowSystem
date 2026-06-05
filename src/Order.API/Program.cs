using Microsoft.EntityFrameworkCore;
using Order.API.Data;
using Order.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<Order.API.StockResponseWorker>();
builder.Services.AddHostedService<OutboxPublisher>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    try
    {
        db.Database.Migrate();
        Console.WriteLine("[DB] Migrations aplicadas com sucesso.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERRO DB]: {ex.Message}");
    }
}

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();
public partial class Program { }
