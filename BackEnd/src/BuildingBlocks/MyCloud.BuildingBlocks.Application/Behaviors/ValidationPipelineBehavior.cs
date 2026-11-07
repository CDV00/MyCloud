using FluentValidation;
using MediatR;
using MyCloud.BuildingBlocks.Domain.Results;

namespace MyCloud.BuildingBlocks.Application.Behaviors;

/// <summary>Chạy FluentValidation; lỗi được trả về dạng ValidationResult thay vì exception.</summary>
public sealed class ValidationPipelineBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        Error[] errors = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .Select(f => new Error(f.PropertyName, f.ErrorMessage))
            .Distinct()
            .ToArray();

        return errors.Length == 0
            ? await next(cancellationToken)
            : CreateValidationResult(errors);
    }

    private static TResponse CreateValidationResult(Error[] errors)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(Result)ValidationResult.WithErrors(errors);
        }

        object validationResult = typeof(ValidationResult<>)
            .MakeGenericType(typeof(TResponse).GenericTypeArguments[0])
            .GetMethod(nameof(ValidationResult.WithErrors))!
            .Invoke(null, [errors])!;

        return (TResponse)validationResult;
    }
}
