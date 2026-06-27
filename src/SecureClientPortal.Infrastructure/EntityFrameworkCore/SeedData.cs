using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecureClientPortal.Backend.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        await db.Database.MigrateAsync();
        await UpsertDefaultRoles(db);

        await UpsertUser(db, CreateSeedUser(SeedGuid("u_admin_001"), "System Admin", "admin@secureportal.local", UserRole.Admin, SerializeClientIds()));
        await UpsertUser(db, CreateSeedUser(SeedGuid("u_acc_001"), "Default Accountant", "accountant@secureportal.local", UserRole.Accountant, SerializeClientIds()));
        await UpsertUser(db, CreateSeedUser(SeedGuid("u_client_001"), "Default Client", "client@secureportal.local", UserRole.Client, SerializeClientIds(SeedGuid("c_001"))));

        var client = await db.Clients.FirstOrDefaultAsync(x => x.Id == SeedGuid("c_001"));
        if (client is null)
        {
            db.Clients.Add(CreateSeedClient());
        }

        await UpsertFilingRule(db, FilingRule.Create(
            SeedGuid("filing_bank_statement"),
            "bank_statement",
            "Business account bank statements eligible for auto-filing.",
            true));
        await UpsertFilingRule(db, FilingRule.Create(
            SeedGuid("filing_invoices"),
            "invoices",
            "Sales and supplier invoice evidence eligible for auto-filing.",
            true));
        await UpsertFilingRule(db, FilingRule.Create(
            SeedGuid("filing_signed_documents"),
            "signed_documents",
            "Signed approvals and authorisations eligible for auto-filing.",
            true));
        await UpsertFilingRule(db, FilingRule.Create(
            SeedGuid("filing_compliance_record"),
            "compliance_record",
            "Compliance support records eligible for auto-filing.",
            true));
        await UpsertFilingRule(db, FilingRule.Create(
            SeedGuid("filing_payroll_summary"),
            "payroll_summary",
            "Payroll summaries and payroll support eligible for auto-filing.",
            true));
        await UpsertFilingRule(db, FilingRule.Create(
            SeedGuid("filing_tax_working_papers"),
            "tax_working_papers",
            "Tax working papers and VAT support eligible for auto-filing.",
            true));
        await UpsertFilingRule(db, FilingRule.Create(
            SeedGuid("filing_proof_of_payment"),
            "proof_of_payment",
            "Payment confirmations and remittance evidence eligible for auto-filing.",
            true));
        await UpsertFilingRule(db, FilingRule.Create(
            SeedGuid("filing_credit_notes"),
            "credit_notes",
            "Credit note support eligible for auto-filing.",
            true));
        await UpsertFilingRule(db, FilingRule.Create(
            SeedGuid("filing_debit_notes"),
            "debit_notes",
            "Debit note support eligible for auto-filing.",
            true));

        await UpsertClientAssignment(db, ClientAssignment.Create(
            SeedGuid("ca_u_acc_001_c_001"),
            SeedGuid("u_acc_001"),
            SeedGuid("c_001")));

        await UpsertMonthlyPack(db, CreateSeedMonthlyPack());
        await UpsertDocumentSlot(db, CreateSeedDocumentSlot(SeedGuid("slot_mp_c001_2026_06_bank_statement"), "bank_statement", "Bank Statement"));
        await UpsertDocumentSlot(db, CreateSeedDocumentSlot(SeedGuid("slot_mp_c001_2026_06_invoices"), "invoices", "Invoices"));

        await UpsertRequiredDocumentTemplate(db, RequiredDocumentTemplate.Create(
            SeedGuid("rdt_bank_statement"),
            "Bank Statement",
            "Default monthly bank statement requirement.",
            "bank_statement",
            true,
            5,
            true));
        await UpsertRequiredDocumentTemplate(db, RequiredDocumentTemplate.Create(
            SeedGuid("rdt_invoices"),
            "Invoices",
            "Default monthly invoice support requirement.",
            "invoices",
            true,
            5,
            true));
        await UpsertRequiredDocumentTemplate(db, RequiredDocumentTemplate.Create(
            SeedGuid("rdt_signed_docs"),
            "Signed Documents",
            "Approvals and signatures where needed.",
            "signed_documents",
            false,
            null,
            true));

        await UpsertMonthlyPackTemplate(db, MonthlyPackTemplate.Create(
            SeedGuid("mpt_default"),
            "Default Monthly Pack",
            "Standard monthly client collection pack.",
            1,
            true));
        await UpsertMonthlyPackTemplateItem(db, MonthlyPackTemplateItem.Create(
            SeedGuid("mpti_default_bank_statement"),
            SeedGuid("mpt_default"),
            SeedGuid("rdt_bank_statement"),
            1));
        await UpsertMonthlyPackTemplateItem(db, MonthlyPackTemplateItem.Create(
            SeedGuid("mpti_default_invoices"),
            SeedGuid("mpt_default"),
            SeedGuid("rdt_invoices"),
            2));

        await UpsertRequestTemplate(db, RequestTemplate.Create(
            SeedGuid("rqt_reupload"),
            "Re-upload Request",
            "reupload_required",
            "Re-upload required: {{documentName}}",
            "{{reason}}",
            "high",
            2,
            true));
        await UpsertRequestTemplate(db, RequestTemplate.Create(
            SeedGuid("rqt_missing"),
            "Missing Document Request",
            "missing_document",
            "Missing document: {{documentName}}",
            "Please upload the required document.",
            "medium",
            3,
            true));
        await UpsertRequestTemplate(db, RequestTemplate.Create(
            SeedGuid("rqt_signature"),
            "Signature Request",
            "signature_required",
            "Signature required: {{documentName}}",
            "Please review and sign the attached item.",
            "medium",
            5,
            true));

        await UpsertReminderRule(db, ReminderRule.Create(
            SeedGuid("rr_deadline_7"),
            "7-day reminder",
            "deadline_approaching",
            7,
            "client",
            "A compliance deadline is due in 7 days.",
            true));
        await UpsertReminderRule(db, ReminderRule.Create(
            SeedGuid("rr_deadline_1"),
            "1-day reminder",
            "deadline_approaching",
            1,
            "client",
            "A compliance deadline is due tomorrow.",
            true));

        await UpsertDeadlineRule(db, DeadlineRule.Create(
            SeedGuid("dr_monthly_pack"),
            "Monthly pack due date",
            "monthly_pack",
            5,
            2,
            "high",
            true));
        await UpsertDeadlineRule(db, DeadlineRule.Create(
            SeedGuid("dr_compliance_item"),
            "Compliance item due date",
            "compliance_item",
            25,
            0,
            "critical",
            true));

        await db.SaveChangesAsync();

        await UpsertComplianceCategory(db, ComplianceCategory.Create(
            SeedGuid("cc_tax_compliance"),
            "Tax Compliance",
            "Income tax, VAT, and tax authority filing obligations.",
            "TAX",
            true));

        await UpsertComplianceCategory(db, ComplianceCategory.Create(
            SeedGuid("cc_cipc_compliance"),
            "CIPC Compliance",
            "Company registration, annual returns, and beneficial ownership obligations.",
            "CIPC",
            true));

        await UpsertComplianceCategory(db, ComplianceCategory.Create(
            SeedGuid("cc_payroll_compliance"),
            "Payroll Compliance",
            "Payroll submissions, UIF, PAYE, and employee record obligations.",
            "PAYROLL",
            true));

        await UpsertComplianceCategory(db, ComplianceCategory.Create(
            SeedGuid("cc_popia_compliance"),
            "POPIA Compliance",
            "Privacy controls, processing evidence, and consent obligations.",
            "POPIA",
            true));
    }

    private static User CreateSeedUser(Guid id, string fullName, string email, UserRole role, string clientIdsJson)
    {
        var user = User.CreateInvited(id, fullName, email, role, PasswordHasher.Hash("Password123!"), clientIdsJson, null);
        user.SetSecurityStatus(SecurityStatus.Active);
        return user;
    }

    private static Client CreateSeedClient()
    {
        var client = Client.Create(SeedGuid("c_001"), "Acme Holdings", "Pty Ltd", "Jane Doe", "jane.doe@acme.test", ClientStatus.Active);
        client.AssignAccountant(SeedGuid("u_acc_001"));
        client.UpdateComplianceHealth(92);
        return client;
    }

    private static MonthlyPack CreateSeedMonthlyPack()
    {
        var pack = MonthlyPack.Create(
            SeedGuid("mp_c001_2026_06"),
            SeedGuid("c_001"),
            2026,
            6);
        pack.MarkDraft();
        return pack;
    }

    private static DocumentSlot CreateSeedDocumentSlot(Guid id, string category, string label)
    {
        var slot = DocumentSlot.Create(
            id,
            SeedGuid("mp_c001_2026_06"),
            SeedGuid("c_001"),
            category,
            label,
            true,
            null);
        slot.MarkMissing();
        return slot;
    }

    private static async Task UpsertUser(PortalDbContext db, User expected)
    {
        var byId = await db.Users.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (byId is null)
        {
            db.Users.Add(expected);
            return;
        }

        byId.SetFullName(expected.FullName);
        byId.SetEmail(expected.Email);
        byId.SetPasswordHash(expected.PasswordHash);
        byId.AssignRole(IdentityDomainValues.ToUserRole(expected.Role));
        byId.SetClientIdsJson(expected.ClientIdsJson);
        byId.SetProfileJson(expected.ProfileJson);
        byId.SetSecurityStatus(SecurityStatus.Active);
    }

    private static async Task UpsertDefaultRoles(PortalDbContext db)
    {
        var activeSystemPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var defaultRole in RolePermissions.DefaultRoles)
        {
            var normalizedPermissions = RolePermissions.NormalizePermissions(defaultRole.Permissions);
            var existing = await db.RoleDefinitions.FirstOrDefaultAsync(x => x.Name == defaultRole.Name);
            if (existing is null)
            {
                db.RoleDefinitions.Add(RoleDefinition.Create(
                    defaultRole.Name,
                    defaultRole.DisplayName,
                    defaultRole.Scope,
                    RolePermissions.SerializePermissions(normalizedPermissions),
                    true,
                    true));
            }
            else
            {
                existing.UpdateDefinition(
                    defaultRole.DisplayName,
                    defaultRole.Scope,
                    RolePermissions.SerializePermissions(normalizedPermissions),
                    true);
                existing.SetActivation(true);
            }

            await UpsertPermissionsAsync(db, normalizedPermissions, true);
            await SyncRolePermissionsAsync(db, defaultRole.Name, normalizedPermissions);
            foreach (var permissionKey in normalizedPermissions)
            {
                activeSystemPermissions.Add(permissionKey);
            }
        }

        var systemPermissions = await db.Permissions.Where(x => x.IsSystemPermission).ToListAsync();
        foreach (var permission in systemPermissions)
        {
            permission.UpdateDetails(
                permission.Name,
                permission.Description,
                permission.IsSystemPermission,
                activeSystemPermissions.Contains(permission.Key));
        }

        await db.SaveChangesAsync();
    }

    private static async Task UpsertFilingRule(PortalDbContext db, FilingRule expected)
    {
        var byId = await db.FilingRules.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (byId is null)
        {
            db.FilingRules.Add(expected);
            return;
        }

        byId.Update(expected.Category, expected.Description, expected.IsEnabled);
    }

    private static async Task UpsertClientAssignment(PortalDbContext db, ClientAssignment expected)
    {
        var byPair = await db.ClientAssignments.FirstOrDefaultAsync(x =>
            x.AccountantUserId == expected.AccountantUserId && x.ClientId == expected.ClientId);
        if (byPair is null)
        {
            db.ClientAssignments.Add(expected);
        }
    }

    private static async Task UpsertMonthlyPack(PortalDbContext db, MonthlyPack expected)
    {
        var existing = await db.MonthlyPacks.FirstOrDefaultAsync(x =>
            x.ClientId == expected.ClientId && x.Year == expected.Year && x.Month == expected.Month);
        if (existing is null)
        {
            db.MonthlyPacks.Add(expected);
            return;
        }

        ApplyMonthlyPackStatus(existing, expected.Status);
    }

    private static void ApplyMonthlyPackStatus(MonthlyPack pack, string status)
    {
        switch (status)
        {
            case "in_progress":
                pack.MarkInProgress();
                break;
            case "submitted":
                pack.MarkSubmitted();
                break;
            case "under_review":
                pack.MarkUnderReview();
                break;
            case "reopened":
                pack.Reopen();
                break;
            case "completed":
                pack.Complete();
                break;
            default:
                pack.MarkDraft();
                break;
        }
    }

    private static async Task UpsertDocumentSlot(PortalDbContext db, DocumentSlot expected)
    {
        var existing = await db.DocumentSlots.FirstOrDefaultAsync(x =>
            x.MonthlyPackId == expected.MonthlyPackId && x.Category == expected.Category);
        if (existing is null)
        {
            db.DocumentSlots.Add(expected);
            return;
        }

        existing.UpdateDefinition(expected.Category, expected.Label, expected.IsRequired);
        existing.UpdateSchedule(expected.DueDateUtc);
        ApplySlotStatus(existing, expected.Status);
    }

    private static void ApplySlotStatus(DocumentSlot slot, string status)
    {
        switch (status)
        {
            case "uploaded":
                if (slot.CurrentDocumentId.HasValue) slot.MarkUploaded(slot.CurrentDocumentId.Value); else slot.MarkMissing();
                break;
            case "under_review":
                slot.MarkUnderReview();
                break;
            case "accepted":
                if (slot.CurrentDocumentId.HasValue) slot.Accept(slot.CurrentDocumentId.Value); else slot.MarkMissing();
                break;
            case "rejected":
                if (slot.CurrentDocumentId.HasValue) slot.Reject(slot.CurrentDocumentId.Value); else slot.MarkMissing();
                break;
            default:
                slot.MarkMissing();
                break;
        }
    }

    private static async Task UpsertComplianceCategory(PortalDbContext db, ComplianceCategory expected)
    {
        var existing = await db.ComplianceCategories.FirstOrDefaultAsync(x => x.Id == expected.Id || x.Code == expected.Code);
        if (existing is null)
        {
            db.ComplianceCategories.Add(expected);
            return;
        }

        existing.UpdateDetails(expected.Name, expected.Description, expected.Code, expected.IsActive);
    }

    private static async Task UpsertPermissionsAsync(PortalDbContext db, IEnumerable<string> permissions, bool isSystemPermission)
    {
        foreach (var permissionKey in permissions)
        {
            var existing = db.Permissions.Local.FirstOrDefault(x =>
                string.Equals(x.Key, permissionKey, StringComparison.OrdinalIgnoreCase))
                ?? await db.Permissions.FirstOrDefaultAsync(x => x.Key == permissionKey);
            if (existing is null)
            {
                db.Permissions.Add(Permission.Create(
                    permissionKey,
                    permissionKey,
                    $"Permission {permissionKey}",
                    isSystemPermission,
                    true));
                continue;
            }

            existing.UpdateDetails(
                permissionKey,
                existing.Description,
                isSystemPermission || existing.IsSystemPermission,
                true);
        }
    }

    private static async Task SyncRolePermissionsAsync(PortalDbContext db, string roleName, IReadOnlyCollection<string> permissions)
    {
        var existing = await db.RolePermissions.Where(x => x.RoleName == roleName).ToListAsync();
        var desired = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

        foreach (var item in existing.Where(x => !desired.Contains(x.PermissionKey)))
        {
            db.RolePermissions.Remove(item);
        }

        foreach (var permissionKey in desired)
        {
            if (existing.Any(x => string.Equals(x.PermissionKey, permissionKey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            db.RolePermissions.Add(RolePermission.Create(
                Guid.NewGuid(),
                roleName,
                permissionKey));
        }
    }

    private static async Task UpsertRequiredDocumentTemplate(PortalDbContext db, RequiredDocumentTemplate expected)
    {
        var existing = await db.RequiredDocumentTemplates.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (existing is null)
        {
            db.RequiredDocumentTemplates.Add(expected);
            return;
        }

        existing.Update(
            expected.Name,
            expected.Description,
            expected.DocumentCategory,
            expected.IsRequired,
            expected.DefaultDueDayOfMonth,
            expected.IsActive);
    }

    private static async Task UpsertMonthlyPackTemplate(PortalDbContext db, MonthlyPackTemplate expected)
    {
        var existing = await db.MonthlyPackTemplates.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (existing is null)
        {
            db.MonthlyPackTemplates.Add(expected);
            return;
        }

        existing.Update(
            expected.Name,
            expected.Description,
            expected.AutoCreateDayOfMonth,
            expected.IsActive);
    }

    private static async Task UpsertMonthlyPackTemplateItem(PortalDbContext db, MonthlyPackTemplateItem expected)
    {
        var existing = await db.MonthlyPackTemplateItems.FirstOrDefaultAsync(x =>
            x.MonthlyPackTemplateId == expected.MonthlyPackTemplateId &&
            x.RequiredDocumentTemplateId == expected.RequiredDocumentTemplateId);
        if (existing is null)
        {
            db.MonthlyPackTemplateItems.Add(expected);
            return;
        }

        existing.UpdateSortOrder(expected.SortOrder);
    }

    private static async Task UpsertRequestTemplate(PortalDbContext db, RequestTemplate expected)
    {
        var existing = await db.RequestTemplates.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (existing is null)
        {
            db.RequestTemplates.Add(expected);
            return;
        }

        existing.Update(
            expected.Name,
            expected.RequestType,
            expected.TitleTemplate,
            expected.DescriptionTemplate,
            expected.Priority,
            expected.DefaultDueInDays,
            expected.IsActive);
    }

    private static async Task UpsertReminderRule(PortalDbContext db, ReminderRule expected)
    {
        var existing = await db.ReminderRules.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (existing is null)
        {
            db.ReminderRules.Add(expected);
            return;
        }

        existing.Update(
            expected.Name,
            expected.TriggerType,
            expected.DaysBeforeDue,
            expected.AudienceRole,
            expected.MessageTemplate,
            expected.IsEnabled);
    }

    private static async Task UpsertDeadlineRule(PortalDbContext db, DeadlineRule expected)
    {
        var existing = await db.DeadlineRules.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (existing is null)
        {
            db.DeadlineRules.Add(expected);
            return;
        }

        existing.Update(
            expected.Name,
            expected.Scope,
            expected.DueDayOfMonth,
            expected.GraceDays,
            expected.Priority,
            expected.IsEnabled);
    }
    private static string SerializeClientIds(params Guid[] clientIds)
    {
        return JsonSerializer.Serialize(clientIds.Select(x => x.ToString()));
    }

    private static Guid SeedGuid(string value)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes($"secure-client-portal:{value}"));
        return new Guid(bytes);
    }
}







