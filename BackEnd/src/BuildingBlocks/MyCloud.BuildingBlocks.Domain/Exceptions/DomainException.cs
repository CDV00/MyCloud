namespace MyCloud.BuildingBlocks.Domain.Exceptions;

/// <summary>Gốc của mọi exception nghiệp vụ. Tầng Web/gRPC map sang HTTP status / gRPC status.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string title, string message)
        : base(message) => Title = title;

    public string Title { get; }
}

/// <summary>Dữ liệu đầu vào vi phạm quy tắc nghiệp vụ → 400 / INVALID_ARGUMENT.</summary>
public abstract class BadRequestException(string message) : DomainException("Bad Request", message);

/// <summary>Không tìm thấy tài nguyên (hoặc người gọi không được phép biết nó tồn tại) → 404 / NOT_FOUND.</summary>
public abstract class NotFoundException(string message) : DomainException("Not Found", message);

/// <summary>Xung đột trạng thái (trùng tên, sai revision...) → 409 / FAILED_PRECONDITION.</summary>
public abstract class ConflictException(string message) : DomainException("Conflict", message);
