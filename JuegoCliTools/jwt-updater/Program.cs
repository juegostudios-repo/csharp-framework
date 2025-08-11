using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JWTUpdater;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var prefix = args.Length > 0 ? args[0] : "JWT";
        
        try
        {
            await UpdateJwtSecret("./appsettings.json");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
    private static string GenerateSecret()
    {
        // Generate a random 256 bits (32 bytes) secret
        // byte[] secret = new byte[32];
        // using (var rng = RandomNumberGenerator.Create())
        // {
        //     rng.GetBytes(secret);
        // }

        // // Base64Url encoding (RFC 7515)
        // string base64 = Convert.ToBase64String(secret)
        //     .TrimEnd('=')
        //     .Replace('+', '-')
        //     .Replace('/', '_');

        // Generate a random 32 varchar string
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var secret = new StringBuilder(32);
        using (var rng = RandomNumberGenerator.Create())
        {
            var data = new byte[32];
            rng.GetBytes(data);
            for (int i = 0; i < data.Length; i++)
            {
                secret.Append(chars[data[i] % (byte)chars.Length]);
            }
        }

        return secret.ToString();
    }

    private static async Task UpdateJwtSecret(string appsettingsPath)
    {
        if (!File.Exists(appsettingsPath))
        {
            throw new FileNotFoundException($"appsettings.json not found at {appsettingsPath}");
        }

        Console.WriteLine("Generating JWT secret");

        var jsonContent = await File.ReadAllTextAsync(appsettingsPath);
        var config = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);

        var newSecret = GenerateSecret();

        if (!config.ContainsKey("AUTH"))
            config["AUTH"] = new Dictionary<string, object>();

        var authSection = JsonSerializer.Deserialize<Dictionary<string, object>>(
            JsonSerializer.Serialize(config["AUTH"]));

        authSection["JWT_SECRET"] = newSecret;
        config["AUTH"] = authSection;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var updatedJson = JsonSerializer.Serialize(config, options);

        await File.WriteAllTextAsync(appsettingsPath, updatedJson);

        Console.WriteLine($"JWT secret updated successfully. New secret: {newSecret}");
    }
}