namespace SecureClientPortal.Backend.Application.Common;

public sealed class AppValidationException : Exception
{
    public AppValidationException(params string[] errors)
        : base(errors.FirstOrDefault() ?? "Validation failed.")
    {
        Errors = errors.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
    }

    public IReadOnlyList<string> Errors { get; }
}
