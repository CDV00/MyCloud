namespace MyCloud.BuildingBlocks.Domain.Results;

public interface IValidationResult
{
    public static readonly Error ValidationError = new("ValidationError", "A validation problem occurred.");

    IReadOnlyList<Error> Errors { get; }
}

public sealed class ValidationResult : Result, IValidationResult
{
    private ValidationResult(IReadOnlyList<Error> errors)
        : base(false, IValidationResult.ValidationError) => Errors = errors;

    public IReadOnlyList<Error> Errors { get; }

    public static ValidationResult WithErrors(IReadOnlyList<Error> errors) => new(errors);
}

public sealed class ValidationResult<TValue> : Result<TValue>, IValidationResult
{
    private ValidationResult(IReadOnlyList<Error> errors)
        : base(default, false, IValidationResult.ValidationError) => Errors = errors;

    public IReadOnlyList<Error> Errors { get; }

    public static ValidationResult<TValue> WithErrors(IReadOnlyList<Error> errors) => new(errors);
}
