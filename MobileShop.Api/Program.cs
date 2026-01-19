using Microsoft.EntityFrameworkCore;
using MobileShop.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Zeabur の PORT 環境変数を読み取る
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// データベース接続文字列を環境変数から取得（複数の環境変数名をチェック）
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
    ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

// デバッグ: すべての環境変数を表示
Console.WriteLine("=== Environment Variables Check ===");
foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
{
    var key = env.Key.ToString();
    if (key.Contains("DATABASE", StringComparison.OrdinalIgnoreCase) || 
        key.Contains("POSTGRES", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase))
    {
        var value = env.Value?.ToString() ?? "";
        var displayValue = value.Length > 30 ? value.Substring(0, 30) + "..." : value;
        Console.WriteLine($"{key} = {displayValue}");
    }
}
Console.WriteLine("===================================");

if (string.IsNullOrEmpty(connectionString))
{
    // 開発環境用のデフォルト接続文字列
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
        ?? "Host=localhost;Database=mobileshop;Username=postgres;Password=postgres";
    Console.WriteLine("⚠️ No DATABASE_URL found - Using default connection string for development");
}
else
{
    Console.WriteLine($"✅ Database connection string found: {connectionString.Substring(0, Math.Min(30, connectionString.Length))}...");
}

// PostgreSQL 接続文字列の変換（必要に応じて）
if (connectionString.StartsWith("postgres://") || connectionString.StartsWith("postgresql://"))
{
    var uri = new Uri(connectionString);
    var userInfo = uri.UserInfo.Split(':');
    var host = uri.Host;
    
    // Zeaburではサフィックスを追加しない（内部DNSをそのまま使用）
    Console.WriteLine($"🔧 Original host: {host}");
    
    connectionString = $"Host={host};Port={uri.Port};Database={uri.AbsolutePath.Trim('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Disable;Include Error Detail=true";
    Console.WriteLine($"🔄 Full connection string: Host={host}, Port={uri.Port}, Database={uri.AbsolutePath.Trim('/')}, Username={userInfo[0]}");
}

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

// Migration の自動実行
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Starting database migration...");
        var context = services.GetRequiredService<ApplicationDbContext>();
        
        // データベース接続を実際に試行
        try
        {
            logger.LogInformation("Attempting to open database connection...");
            await context.Database.OpenConnectionAsync();
            logger.LogInformation("✅ Database connection SUCCESS!");
            await context.Database.CloseConnectionAsync();
            
            logger.LogInformation("Running migrations...");
            await context.Database.MigrateAsync();
            logger.LogInformation("✅ Database migration completed successfully.");
        }
        catch (Exception connEx)
        {
            logger.LogError(connEx, "❌ Database connection failed: {Message}", connEx.Message);
            logger.LogError("Connection error type: {Type}", connEx.GetType().FullName);
            if (connEx.InnerException != null)
            {
                logger.LogError("Inner exception: {InnerMessage}", connEx.InnerException.Message);
                logger.LogError("Inner exception type: {InnerType}", connEx.InnerException.GetType().FullName);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating the database: {Message}", ex.Message);
        logger.LogError("Inner exception: {InnerException}", ex.InnerException?.Message);
    }
}

// Swagger を本番環境でも有効化
app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();
app.MapControllers();

app.Run();
