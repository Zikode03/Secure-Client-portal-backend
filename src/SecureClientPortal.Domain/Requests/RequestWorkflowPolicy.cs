namespace SecureClientPortal.Backend.Domain.Modules.Requests;

using SecureClientPortal.Backend.Domain.Shared.Modules.Requests;
using SecureClientPortal.Backend.Models;

public static class RequestWorkflowPolicy
{
    private static readonly HashSet<RequestStatus> FirmEditableStatuses =
    [
        RequestStatus.Open,
        RequestStatus.WaitingOnClient,
        RequestStatus.WaitingOnAccountant,
        RequestStatus.Resolved
    ];

    public static RequestStatus DetermineInitialStatus(WorkflowActorContext actor)
    {
        return actor.IsClient ? RequestStatus.WaitingOnAccountant : RequestStatus.WaitingOnClient;
    }

    public static void ApplyCommentTransition(RequestItem request, WorkflowActorContext actor)
    {
        if (actor.IsClient)
        {
            request.MarkWaitingOnAccountant();
            return;
        }

        request.MarkWaitingOnClient();
    }

    public static string NormalizeExternalStatus(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch
        {
            "awaiting_client" => "waiting_on_client",
            "awaiting_accountant" => "waiting_on_accountant",
            _ => normalized
        };
    }

    public static bool CanManuallySetStatus(WorkflowActorContext actor, RequestStatus status)
    {
        if (actor.IsClient)
        {
            return false;
        }

        return FirmEditableStatuses.Contains(status);
    }

    public static void RefreshOverdue(IEnumerable<RequestItem> requests, DateTime now)
    {
        foreach (var item in requests.Where(x => x.ShouldBeMarkedOverdue(now)))
        {
            item.MarkOverdue();
        }
    }
}
