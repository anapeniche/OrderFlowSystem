using Microsoft.EntityFrameworkCore;
using Stock.API.Data;
using Stock.API;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<StockDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StockDbContext>();
    try
    {
        db.Database.Migrate();
        Console.WriteLine("[DB] Stock migrations aplicadas com sucesso.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERRO DB STOCK]: {ex.Message}");
    }
}

host.Run();