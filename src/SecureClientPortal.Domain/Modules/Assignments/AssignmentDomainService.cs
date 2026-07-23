using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Domain.Modules.Assignments;

public sealed class AssignmentDomainService
{
    public ClientAssignment? SelectReplacementPrimary(
        Client client,
        ClientAssignment assignmentBeingRemoved,
        IReadOnlyCollection<ClientAssignment> assignments)
    {
        if (assignmentBeingRemoved.ClientId != client.Id)
        {
            throw new DomainRuleException("The assignment does not belong to the selected client.");
        }

        var isPrimary = client.AssignedAccountantId == assignmentBeingRemoved.AccountantUserId;
        if (!isPrimary)
        {
            return null;
        }

        return assignments
            .Where(x => x.ClientId == client.Id && x.Id != assignmentBeingRemoved.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefault()
            ?? throw new DomainRuleException("Cannot remove the only primary accountant assignment for a client.");
    }
}
