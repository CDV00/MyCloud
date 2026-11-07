using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using Grpc.Net.Client;
using MyCloud.Contracts.Storage.V1;
using MyCloud.Testing;

namespace Storage.IntegrationTests;

// Phase 4: Drive gọi Storage bằng định danh service thay cho chủ sở hữu (ADR-014).
public sealed partial class StorageTests
{
    [Fact]
    public async Task ServiceToken_ActsForOwner_ButOnlyTheUploaderCanSignParts()
    {
        Guid owner = Guid.NewGuid(), editor = Guid.NewGuid();
        StorageService.StorageServiceClient service = ServiceClient();

        CreateUploadSessionResponse session = await service.CreateUploadSessionAsync(new CreateUploadSessionRequest
        {
            UserId = owner.ToString(),
            UploaderId = editor.ToString(),
            SizeBytes = 1000,
        });

        (await QuotaAsync(owner)).ReservedBytes.ShouldBe(1000); // chủ sở hữu trả dung lượng
        (await QuotaAsync(editor)).ReservedBytes.ShouldBe(0);

        using HttpClient editorRest = factory.CreateClient().AsUser(editor);
        (await editorRest.PostAsJsonAsync($"/api/v1/uploads/{session.SessionId}/part-urls", new { partNumbers = new[] { 1 } }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        using HttpClient ownerRest = factory.CreateClient().AsUser(owner);
        (await ownerRest.PostAsJsonAsync($"/api/v1/uploads/{session.SessionId}/part-urls", new { partNumbers = new[] { 1 } }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserToken_CannotActForSomeoneElse_EvenAsUploader()
    {
        Guid user = Guid.NewGuid();
        RpcException ex = await Should.ThrowAsync<RpcException>(() => factory.GrpcClientFor(user).CreateUploadSessionAsync(
            new CreateUploadSessionRequest { UserId = Guid.NewGuid().ToString(), UploaderId = user.ToString(), SizeBytes = 1 }).ResponseAsync);
        ex.StatusCode.ShouldBe(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task ServiceToken_CopiesAnotherOwnersBlob_IntoTheRecipientsQuota()
    {
        Guid owner = Guid.NewGuid(), recipient = Guid.NewGuid();
        byte[] content = RandomBytes(2048);
        string source = await UploadAsync(factory.GrpcClientFor(owner), owner, content);

        CopyBlobResponse copy = await ServiceClient().CopyBlobAsync(new CopyBlobRequest
        {
            UserId = recipient.ToString(),
            SourceUserId = owner.ToString(),
            SourceBlobId = source,
        });

        copy.SizeBytes.ShouldBe(content.Length);
        (await QuotaAsync(recipient)).UsedBytes.ShouldBe(content.Length);
        (await QuotaAsync(owner)).UsedBytes.ShouldBe(content.Length);
    }

    private StorageService.StorageServiceClient ServiceClient() =>
        new(GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler(),
            Credentials = ChannelCredentials.Create(
                ChannelCredentials.Insecure,
                CallCredentials.FromInterceptor((_, metadata) =>
                {
                    metadata.Add("Authorization", $"Bearer {TestJwt.CreateServiceToken()}");
                    return Task.CompletedTask;
                })),
            UnsafeUseInsecureChannelCallCredentials = true,
        }));
}
