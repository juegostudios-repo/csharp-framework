using Amazon.S3;
using Amazon.S3.Model;

namespace JuegoFramework.Helpers;

public class AWSSimpleStorage
{
    private static readonly string bucketName = Environment.GetEnvironmentVariable("AWS_S3_BUCKET_NAME") ?? "";
    private static readonly string filePath = Environment.GetEnvironmentVariable("AWS_S3_FILE_PATH") ?? "";
    private static readonly string accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "";
    private static readonly string secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "";
    private static readonly string region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "";

    private static readonly AmazonS3Config s3Config = new()
    {
        SignatureVersion = "v4",
        RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
    };

    private static readonly AmazonS3Client s3Client = GetAmazonS3Client(accessKey, secretKey, s3Config);

    public static string GetPresignedURL(string fileName, int expirySeconds)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = $"{filePath}/{fileName}",
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddSeconds(expirySeconds)
        };
        return s3Client.GetPreSignedURL(request);
    }

    public static string GetUrl(string path)
    {
        return $"https://{bucketName}.s3.{region}.amazonaws.com/{filePath}/{path}";
    }

    private static AmazonS3Client GetAmazonS3Client(string accessKey, string secretKey, AmazonS3Config s3Config)
    {
        if (accessKey == "" || secretKey == "")
        {
            return new AmazonS3Client(s3Config);
        }

        return new AmazonS3Client(accessKey, secretKey, s3Config);
    }
}
