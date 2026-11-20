using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ToeicSpace.BuildingBlocks.Storage.Abstractions;
using ToeicSpace.BuildingBlocks.Storage.CloudflareR2;

namespace ToeicSpace.BuildingBlocks.Storage.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCloudflareR2Storage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CloudflareR2Options>(
            configuration.GetSection(CloudflareR2Options.SectionName));

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudflareR2Options>>().Value;
            if (string.IsNullOrWhiteSpace(options.Endpoint) || string.IsNullOrWhiteSpace(options.AccessKey) ||
                string.IsNullOrWhiteSpace(options.SecretKey) || string.IsNullOrWhiteSpace(options.BucketName))
                throw new InvalidOperationException(
                    "R2 storage requires R2_ENDPOINT, R2_ACCESS_KEY, R2_SECRET_KEY and R2_BUCKET_NAME.");

            return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey),
                new AmazonS3Config { ServiceURL = options.Endpoint, ForcePathStyle = true });
        });

        services.AddScoped<IObjectStorageService, R2ObjectStorageService>();

        return services;
    }
}