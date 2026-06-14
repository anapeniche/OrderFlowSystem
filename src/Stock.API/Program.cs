using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using Stock.API.Data;
using Stock.API;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Stock.API")
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog();

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
            Log.Information("Stock migrations aplicadas com sucesso");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao aplicar Stock migrations");
        }
    }

    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Stock.API encerrou inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}
