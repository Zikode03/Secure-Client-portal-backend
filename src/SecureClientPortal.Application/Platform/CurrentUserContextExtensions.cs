using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Application;

public static class CurrentUserContextExtensions
{
    public static WorkflowActorContext ToWorkflowActorContext(this CurrentUserContext currentUser)
    {
        return WorkflowActorContext.Create(currentUser.UserId, currentUser.RoleScope, currentUser.Permissions);
    }
}
