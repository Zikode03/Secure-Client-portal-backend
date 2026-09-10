namespace SecureClientPortal.Backend.Models;

public class RequestReadState
{
    public Guid Id { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime LastReadAtUtc { get; private set; }

    public static RequestReadState Create(Guid requestId, Guid clientId, Guid userId, DateTime? readAtUtc = null)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("Request id is required.", nameof(requestId));
        if (clientId == Guid.Empty) throw new ArgumentException("Client id is required.", nameof(clientId));
        if (userId == Guid.Empty) throw new ArgumentException("User id is required.", nameof(userId));

        return new RequestReadState
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ClientId = clientId,
            UserId = userId,
            LastReadAtUtc = readAtUtc ?? DateTime.UtcNow
        };
    }

    public void MarkRead(DateTime? readAtUtc = null)
    {
        LastReadAtUtc = readAtUtc ?? DateTime.UtcNow;
    }
}
