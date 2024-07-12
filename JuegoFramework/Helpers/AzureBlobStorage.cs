using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace JuegoFramework.Helpers
{
    public class AzureBlobStorage
    {
        private static readonly string accountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? "";
        private static readonly string accountKey = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_KEY") ?? "";
        private static readonly string containerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") ?? "";

        public static string GetPresignedURL(string fileName, int expirySeconds)
        {
            try
            {
                var credential = new StorageSharedKeyCredential(accountName, accountKey);
                var blobServiceClient = new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net"), credential);
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(fileName);

                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = containerName,
                    BlobName = fileName,
                    Resource = "b",
                    StartsOn = DateTimeOffset.UtcNow,
                    ExpiresOn = DateTimeOffset.UtcNow.AddSeconds(expirySeconds)
                };
                sasBuilder.SetPermissions(BlobSasPermissions.Read | BlobSasPermissions.Add | BlobSasPermissions.Create | BlobSasPermissions.Write | BlobSasPermissions.Delete);

                var sasQueryParameters = sasBuilder.ToSasQueryParameters(credential);
                var sasUrl = $"{blobClient.Uri}?{sasQueryParameters}";

                return sasUrl;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error: GetPresignedURL - {ex}");
            }
        }

        public static string GetUrl(string path)
        {
            return $"https://{accountName}.blob.core.windows.net/{containerName}/{path}";
        }
    }
}
