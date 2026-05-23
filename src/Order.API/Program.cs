using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<Order.API.StockResponseWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DbContext>();
    try 
    {
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Orders"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""CustomerId"" INTEGER NOT NULL,
                ""OrderDate"" TIMESTAMP NOT NULL DEFAULT NOW(),
                ""Status"" INTEGER NOT NULL DEFAULT 0
            );
        ");
    } 
    catch (System.Exception ex) 
    {
        System.Console.WriteLine($"[ERRO DB]: {ex.Message}");
    }
}

app.UseSwagger();
app.UseSwaggerUI();


app.MapControllers();
app.Run();
