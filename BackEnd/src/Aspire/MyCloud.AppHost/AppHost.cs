using Aspire.Hosting.ApplicationModel;

// Chạy toàn bộ MyCloud ở môi trường dev:  dotnet run --project src/Aspire/MyCloud.AppHost
// Dashboard (log, trace, metric, trạng thái container) mở theo URL in ra console.
var builder = DistributedApplication.CreateBuilder(args);

// ---------- Hạ tầng (container giữ lại giữa các lần chạy để khởi động nhanh) ----------
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var identityDb = postgres.AddDatabase("identitydb", databaseName: "identity");
var driveDb = postgres.AddDatabase("drivedb", databaseName: "drive");
var storageDb = postgres.AddDatabase("storagedb", databaseName: "storage");
var sharingDb = postgres.AddDatabase("sharingdb", databaseName: "sharing");
var notificationDb = postgres.AddDatabase("notificationdb", databaseName: "notification");

// Secret client credentials của Drive (ADR-014): Aspire sinh ngẫu nhiên lần đầu và lưu trong user secrets của AppHost.
var driveClientSecret = builder.AddParameter(
    "drive-client-secret", new GenerateParameterDefault { MinLength = 40, Special = false }, secret: true, persist: true);

var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var searchDb = mongo.AddDatabase("searchdb", databaseName: "search");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin(port: 15672) // cổng cố định: menu quản lý ở http://localhost/
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var redis = builder.AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent);

// Image minio/minio đã bị gỡ khỏi Docker Hub/Quay (MinIO ngừng phát hành image bản community).
// Dùng tạm bản build từ source của Chainguard, ghim theo digest; lựa chọn lâu dài quyết định ở Phase 2 (ADR-011).
var minio = builder.AddMinioContainer("minio")
    .WithImage("chainguard/minio", "latest")
    .WithImageRegistry("cgr.dev")
    .WithImageSHA256("aea6c163749e1caca37f907976dcf337177ddf5f558808f2aa591618fd3a6eef")
    .WithEndpoint("console", e => e.Port = 9001)
    // API S3 cố định ở 9000: Vite (dev) và nginx proxy /mycloud-blobs/ tới đây để trình duyệt upload/tải cùng origin (ADR-013).
    .WithEndpoint("http", e => e.Port = 9000)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// Persistent: giữ cổng cố định 8025 mà không bị container cũ (bị bỏ lại khi AppHost tắt đột ngột) chiếm chỗ.
var mailpit = builder.AddMailPit("mailpit", httpPort: 8025)
    .WithLifetime(ContainerLifetime.Persistent);

// ---------- Service ----------
// Identity tự sinh khoá ký RS256 (lưu DB, mã hoá bằng Data Protection) và công bố JWKS.
// Mọi service khác tham chiếu "identity" để tải JWKS qua service discovery và tự validate token.
var identity = builder.AddProject<Projects.Identity_Api>("identity")
    .WithReference(identityDb).WaitFor(identityDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithEnvironment("ServiceClients__Clients__drive__Secret", driveClientSecret);

// Storage: REST (qua Gateway) + gRPC "grpc" (HTTP/2, chỉ Drive gọi) — cả hai khai báo ở Kestrel:Endpoints của project.
var storage = builder.AddProject<Projects.Storage_Api>("storage")
    .WithReference(storageDb).WaitFor(storageDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(minio).WaitFor(minio)
    .WithReference(identity);

var sharing = builder.AddProject<Projects.Sharing_Api>("sharing")
    .WithReference(sharingDb).WaitFor(sharingDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(identity);

// Drive gọi Storage / Sharing / Identity bằng định danh service của chính nó (client credentials).
var drive = builder.AddProject<Projects.Drive_Api>("drive")
    .WithReference(driveDb).WaitFor(driveDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(storage).WaitFor(storage)
    .WithReference(sharing).WaitFor(sharing)
    .WithReference(identity).WaitFor(identity)
    .WithEnvironment("ServiceIdentity__ClientId", "drive")
    .WithEnvironment("ServiceIdentity__ClientSecret", driveClientSecret);

var search = builder.AddProject<Projects.Search_Api>("search")
    .WithReference(searchDb).WaitFor(searchDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(identity);

var notification = builder.AddProject<Projects.Notification_Api>("notification")
    .WithReference(notificationDb).WaitFor(notificationDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(redis).WaitFor(redis) // SignalR backplane
    .WithReference(mailpit)
    .WithReference(identity);

builder.AddProject<Projects.Media_Worker>("media")
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(minio);

var gateway = builder.AddProject<Projects.MyCloud_Gateway>("gateway")
    .WithReference(identity).WaitFor(identity)
    .WithReference(drive)
    .WithReference(storage)
    .WithReference(sharing)
    .WithReference(search)
    .WithReference(notification)
    .WithReference(redis)
    .WithExternalHttpEndpoints();

// ---------- Web (Vite dev server) ----------
// Trình duyệt chỉ nói chuyện với Vite (cùng origin); Vite proxy /api sang Gateway ⇒ cookie refresh token
// SameSite=Strict hoạt động và không cần CORS. Production: reverse proxy phục vụ SPA + /api trên cùng domain.
builder.AddViteApp("web", "../../../../FrontEnd/web")
    .WithReference(gateway).WaitFor(gateway)
    .WithEnvironment("MINIO_URL", minio.GetEndpoint("http"))
    .WithEndpoint("http", e => e.Port = 5173) // cổng cố định, quen thuộc với Vite
    .WithExternalHttpEndpoints();

// Container của Aspire chỉ nghe trên 127.0.0.1, còn "localhost" trên Windows phân giải ra ::1 trước: .NET thử IPv6 tuần tự
// nên mỗi kết nối MỚI tới Postgres/MinIO/RabbitMQ mất ~2 s (đo được 2047 ms so với 12 ms) — đủ làm lời gọi gRPC
// (deadline 2 s) thất bại ngẫu nhiên. Chỉ áp dụng khi chạy bằng AppHost ở máy dev.
foreach (ProjectResource project in builder.Resources.OfType<ProjectResource>().ToList())
{
    builder.CreateResourceBuilder(project).WithEnvironment("DOTNET_SYSTEM_NET_DISABLEIPV6", "1");
}

builder.Build().Run();
