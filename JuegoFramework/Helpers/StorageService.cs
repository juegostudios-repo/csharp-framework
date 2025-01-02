namespace JuegoFramework.Helpers;

public class StorageService
{
    public static string GetPresignedURL(string fileName, int expirySeconds)
    {
        if (Environment.GetEnvironmentVariable("USE_STORAGE_SYSTEM") == "AZURE")
        {
            return AzureBlobStorage.GetPresignedURL(fileName, expirySeconds);
        }

        if (Environment.GetEnvironmentVariable("USE_STORAGE_SYSTEM") == "AWS")
        {
            return AWSSimpleStorage.GetPresignedURL(fileName, expirySeconds);
        }

        throw new Exception("Please define USE_STORAGE_SYSTEM environment variable");
    }

    public static string GetUrl(string path)
    {
        if (path == string.Empty)
        {
            return string.Empty;
        }

        if (Environment.GetEnvironmentVariable("USE_STORAGE_SYSTEM") == "AZURE")
        {
            return AzureBlobStorage.GetUrl(path);
        }

        if (Environment.GetEnvironmentVariable("USE_STORAGE_SYSTEM") == "AWS")
        {
            return AWSSimpleStorage.GetUrl(path);
        }

        throw new Exception("Please define USE_STORAGE_SYSTEM environment variable");
    }
}
