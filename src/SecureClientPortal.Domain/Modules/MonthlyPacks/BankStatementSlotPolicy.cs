namespace SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;

public static class BankStatementSlotPolicy
{
    public static void Apply(DocumentSlot slot, bool connected, bool hasEvidence)
    {
        if (connected)
        {
            slot.UpdateDefinition(slot.Category, slot.Label, !hasEvidence);
            if (!hasEvidence && slot.Status is "not_started" or "not_applicable")
                slot.MarkNotApplicable();
        }
        else
        {
            slot.UpdateDefinition(slot.Category, slot.Label, true);
            if (!hasEvidence && slot.Status == "not_applicable")
                slot.MarkNotStarted();
        }
    }
}
