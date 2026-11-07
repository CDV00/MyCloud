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

var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var searchDb = mongo.AddDatabase("searchdb", databaseName: "search");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
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
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var mailpit = builder.AddMailPit("mailpit");

// Khoá ký JWT tạm thời của Phase 0 (HS256): Aspire sinh ngẫu nhiên và lưu vào user-secrets của AppHost,
// không bao giờ nằm trong appsettings/git. Phase 1 thay bằng RS256 + JWKS.
var jwtSigningKey = builder.AddParameter(
    "jwt-signing-key",
    new GenerateParameterDefault { MinLength = 64, Special = false },
    secret: true,
    persist: true);

// ---------- Service ----------
var identity = builder.AddProject<Projects.Identity_Api>("identity")
    .WithReference(identityDb).WaitFor(identityDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithEnvironment("Jwt__SigningKey", jwtSigningKey);

var drive = builder.AddProject<Projects.Drive_Api>("drive")
    .WithReference(driveDb).WaitFor(driveDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq);

var storage = builder.AddProject<Projects.Storage_Api>("storage")
    .WithReference(minio).WaitFor(minio);

var sharing = builder.AddProject<Projects.Sharing_Api>("sharing");

var search = builder.AddProject<Projects.Search_Api>("search")
    .WithReference(searchDb).WaitFor(searchDb)
    .WithReference(rabbitmq).WaitFor(rabbitmq);

var notification = builder.AddProject<Projects.Notification_Api>("notification")
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(mailpit);

builder.AddProject<Projects.Media_Worker>("media")
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(minio);

builder.AddProject<Projects.MyCloud_Gateway>("gateway")
    .WithReference(identity)
    .WithReference(drive)
    .WithReference(storage)
    .WithReference(sharing)
    .WithReference(search)
    .WithReference(notification)
    .WithReference(redis)
    .WithExternalHttpEndpoints();

builder.Build().Run();
