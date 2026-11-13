using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LocalServicesMarketplace.Data;

/// <summary>
/// Lets "dotnet ef migrations add" build the model without starting the web host.
/// Commands that touch a database (e.g. "database update") read LSM_CONNECTION first.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("LSM_CONNECTION")
            ?? "Server=127.0.0.1,14333;Database=LocalServicesMarketplace;User Id=sa;Password=Lsm_Dev_Passw0rd!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        return new ApplicationDbContext(options);
    }
}
