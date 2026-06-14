using Microsoft.EntityFrameworkCore;
using Order.API.Data;
using Order.API.Services;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Order.API")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

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
            Log.Information("Migrations aplicadas com sucesso");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao aplicar migrations");
        }
    }

    app.UseSerilogRequestLogging();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapControllers();
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Order.API encerrou inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
