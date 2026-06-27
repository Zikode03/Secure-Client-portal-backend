namespace SecureClientPortal.Backend.Models;

public static class RequestWorkflowPolicy
{
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

    public static void RefreshOverdue(IEnumerable<RequestItem> requests, DateTime now)
    {
        foreach (var item in requests.Where(x => x.ShouldBeMarkedOverdue(now)))
        {
            item.MarkOverdue();
        }
    }
}
