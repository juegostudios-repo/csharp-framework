using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ProjectName.Migrations
{
    public class AppDbContext : DbContext
    {
        public AppDbContext()
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            string entityNamespace = "ProjectName.Models";

            Assembly assembly = Assembly.GetExecutingAssembly()!;

            // assembly.GetTypes().ToList().ForEach(t => Console.WriteLine(t.FullName));

            var entityTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && string.Equals(t.Namespace, entityNamespace, StringComparison.Ordinal) && t.GetCustomAttribute<TableAttribute>() != null);

            Console.WriteLine($"Found {entityTypes.Count()} entities in namespace {entityNamespace}");

            foreach (var type in entityTypes)
            {
                Console.WriteLine($"Adding entity type: {type.Name}");
                modelBuilder.Entity(type);
            }
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            if (!options.IsConfigured)
            {
                string? connectionString = Global.Configuration?.GetConnectionString("DefaultConnection");

                if (connectionString == null)
                {
                    Log.Information("Only for ef migrations - bug if called outside");
                    IConfigurationRoot configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .Build();
                    connectionString = configuration.GetConnectionString("DefaultConnection");

                    var dbConnectionStringEnv = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        connectionString = dbConnectionStringEnv;
                    }
                    else
                    {
                        Log.Information(connectionString ?? "No Connection String Found");
                    }
                }

                options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
                .LogTo(Log.Information, LogLevel.Information);

                if (Global.Environment != null && Global.Environment.IsDevelopment())
                {
                    options.EnableSensitiveDataLogging();
                    options.EnableDetailedErrors();
                }
            }
        }

    }
}
