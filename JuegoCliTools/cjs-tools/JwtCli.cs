using System.IO;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace CjsTools;
public class JwtCli
{
    public static async Task<int> UpdateJwt(string appsettingsPath)
    {
        try
        {
            await UpdateJwtSecret(appsettingsPath);
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"Error: {ex.Message}");
            return 1;
        }
    }
    private static string GenerateSecret()
    {
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
            throw new FileNotFoundException($"file not found at {appsettingsPath}");
        }

        Logger.Log("Generating JWT secret");

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

        Logger.Info($"JWT secret updated successfully. New secret: {newSecret}");
    }
}
