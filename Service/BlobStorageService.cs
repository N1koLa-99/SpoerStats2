using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace SpoerStats2.Services
{
    public class BlobStorageService
    {
        private readonly BlobServiceClient _svc;
        private readonly string _container = "message-attachments";

        public BlobStorageService(BlobServiceClient svc) { _svc = svc; }

        public async Task<(string url, string contentType)> UploadAsync(Stream s, string fileName, string? ct = null, CancellationToken token = default)
        {
            var container = _svc.GetBlobContainerClient(_container);
            await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: token);

            var blobName = $"{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}_{fileName}";
            var client = container.GetBlobClient(blobName);

            var headers = new BlobHttpHeaders { ContentType = ct ?? "application/octet-stream" };
            await client.UploadAsync(s, new BlobUploadOptions { HttpHeaders = headers }, token);

            return (client.Uri.ToString(), headers.ContentType);
        }
    }
}
