namespace SecureClientPortal.Backend.Application.Common;

public sealed class AppForbiddenException(string message) : Exception(message);
