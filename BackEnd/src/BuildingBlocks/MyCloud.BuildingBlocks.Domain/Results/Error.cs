namespace MyCloud.BuildingBlocks.Domain.Results;

/// <summary>Lỗi nghiệp vụ có mã ổn định (ví dụ <c>DRIVE.FOLDER_NOT_FOUND</c>) để client/i18n dựa vào.</summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static readonly Error NullValue = new("Error.NullValue", "The specified result value is null.");

    public override string ToString() => Code;
}
