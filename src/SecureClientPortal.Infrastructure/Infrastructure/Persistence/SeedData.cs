using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Models;

namespace SecureClientPortal.Backend.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        await db.Database.MigrateAsync();
        await UpsertDefaultRoles(db);

        await UpsertUser(db, new User
        {
            Id = "u_admin_001",
            FullName = "System Admin",
            Email = "admin@secureportal.local",
            PasswordHash = PasswordHasher.Hash("Password123!"),
            Role = "admin",
            ClientIdsJson = "[]"
        });

        await UpsertUser(db, new User
        {
            Id = "u_acc_001",
            FullName = "Default Accountant",
            Email = "accountant@secureportal.local",
            PasswordHash = PasswordHasher.Hash("Password123!"),
            Role = "accountant",
            ClientIdsJson = "[]"
        });

        await UpsertUser(db, new User
        {
            Id = "u_client_001",
            FullName = "Default Client",
            Email = "client@secureportal.local",
            PasswordHash = PasswordHasher.Hash("Password123!"),
            Role = "client",
            ClientIdsJson = "[\"c_001\"]"
        });

        var client = await db.Clients.FirstOrDefaultAsync(x => x.Id == "c_001");
        if (client is null)
        {
            db.Clients.Add(new Client
            {
                Id = "c_001",
                Name = "Acme Holdings",
                EntityType = "Pty Ltd",
                Status = "active",
                ComplianceHealth = 92,
                AssignedAccountantId = "u_acc_001",
                PrimaryContact = "Jane Doe",
                Email = "jane.doe@acme.test"
            });
        }

        await UpsertFilingRule(db, new FilingRule
        {
            Id = "filing_bank_statement",
            Category = "bank_statement",
            Description = "Business account bank statements eligible for auto-filing.",
            IsEnabled = true,
        });
        await UpsertFilingRule(db, new FilingRule
        {
            Id = "filing_invoices",
            Category = "invoices",
            Description = "Sales and supplier invoice evidence eligible for auto-filing.",
            IsEnabled = true,
        });
        await UpsertFilingRule(db, new FilingRule
        {
            Id = "filing_signed_documents",
            Category = "signed_documents",
            Description = "Signed approvals and authorisations eligible for auto-filing.",
            IsEnabled = true,
        });
        await UpsertFilingRule(db, new FilingRule
        {
            Id = "filing_compliance_record",
            Category = "compliance_record",
            Description = "Compliance support records eligible for auto-filing.",
            IsEnabled = true,
        });
        await UpsertFilingRule(db, new FilingRule
        {
            Id = "filing_payroll_summary",
            Category = "payroll_summary",
            Description = "Payroll summaries and payroll support eligible for auto-filing.",
            IsEnabled = true,
        });
        await UpsertFilingRule(db, new FilingRule
        {
            Id = "filing_tax_working_papers",
            Category = "tax_working_papers",
            Description = "Tax working papers and VAT support eligible for auto-filing.",
            IsEnabled = true,
        });
        await UpsertFilingRule(db, new FilingRule
        {
            Id = "filing_proof_of_payment",
            Category = "proof_of_payment",
            Description = "Payment confirmations and remittance evidence eligible for auto-filing.",
            IsEnabled = true,
        });
        await UpsertFilingRule(db, new FilingRule
        {
            Id = "filing_credit_notes",
            Category = "credit_notes",
            Description = "Credit note support eligible for auto-filing.",
            IsEnabled = true,
        });
        await UpsertFilingRule(db, new FilingRule
        {
            Id = "filing_debit_notes",
            Category = "debit_notes",
            Description = "Debit note support eligible for auto-filing.",
            IsEnabled = true,
        });

        await UpsertClientAssignment(db, new ClientAssignment
        {
            Id = "ca_u_acc_001_c_001",
            AccountantUserId = "u_acc_001",
            ClientId = "c_001"
        });

        await UpsertMonthlyPack(db, new MonthlyPack
        {
            Id = "mp_c001_2026_06",
            ClientId = "c_001",
            Year = 2026,
            Month = 6,
            Status = "draft"
        });

        await UpsertDocumentSlot(db, new DocumentSlot
        {
            Id = "slot_mp_c001_2026_06_bank_statement",
            MonthlyPackId = "mp_c001_2026_06",
            ClientId = "c_001",
            Category = "bank_statement",
            Label = "Bank Statement",
            IsRequired = true,
            Status = "missing"
        });

        await UpsertDocumentSlot(db, new DocumentSlot
        {
            Id = "slot_mp_c001_2026_06_invoices",
            MonthlyPackId = "mp_c001_2026_06",
            ClientId = "c_001",
            Category = "invoices",
            Label = "Invoices",
            IsRequired = true,
            Status = "missing"
        });

        await UpsertRequiredDocumentTemplate(db, new RequiredDocumentTemplate
        {
            Id = "rdt_bank_statement",
            Name = "Bank Statement",
            Description = "Default monthly bank statement requirement.",
            DocumentCategory = "bank_statement",
            IsRequired = true,
            DefaultDueDayOfMonth = 5,
            IsActive = true
        });
        await UpsertRequiredDocumentTemplate(db, new RequiredDocumentTemplate
        {
            Id = "rdt_invoices",
            Name = "Invoices",
            Description = "Default monthly invoice support requirement.",
            DocumentCategory = "invoices",
            IsRequired = true,
            DefaultDueDayOfMonth = 5,
            IsActive = true
        });
        await UpsertRequiredDocumentTemplate(db, new RequiredDocumentTemplate
        {
            Id = "rdt_signed_docs",
            Name = "Signed Documents",
            Description = "Approvals and signatures where needed.",
            DocumentCategory = "signed_documents",
            IsRequired = false,
            DefaultDueDayOfMonth = null,
            IsActive = true
        });

        await UpsertMonthlyPackTemplate(db, new MonthlyPackTemplate
        {
            Id = "mpt_default",
            Name = "Default Monthly Pack",
            Description = "Standard monthly client collection pack.",
            AutoCreateDayOfMonth = 1,
            IsActive = true
        });
        await UpsertMonthlyPackTemplateItem(db, new MonthlyPackTemplateItem
        {
            Id = "mpti_default_bank_statement",
            MonthlyPackTemplateId = "mpt_default",
            RequiredDocumentTemplateId = "rdt_bank_statement",
            SortOrder = 1
        });
        await UpsertMonthlyPackTemplateItem(db, new MonthlyPackTemplateItem
        {
            Id = "mpti_default_invoices",
            MonthlyPackTemplateId = "mpt_default",
            RequiredDocumentTemplateId = "rdt_invoices",
            SortOrder = 2
        });

        await UpsertRequestTemplate(db, new RequestTemplate
        {
            Id = "rqt_reupload",
            Name = "Re-upload Request",
            RequestType = "reupload_required",
            TitleTemplate = "Re-upload required: {{documentName}}",
            DescriptionTemplate = "{{reason}}",
            Priority = "high",
            DefaultDueInDays = 2,
            IsActive = true
        });
        await UpsertRequestTemplate(db, new RequestTemplate
        {
            Id = "rqt_missing",
            Name = "Missing Document Request",
            RequestType = "missing_document",
            TitleTemplate = "Missing document: {{documentName}}",
            DescriptionTemplate = "Please upload the required document.",
            Priority = "medium",
            DefaultDueInDays = 3,
            IsActive = true
        });
        await UpsertRequestTemplate(db, new RequestTemplate
        {
            Id = "rqt_signature",
            Name = "Signature Request",
            RequestType = "signature_required",
            TitleTemplate = "Signature required: {{documentName}}",
            DescriptionTemplate = "Please review and sign the attached item.",
            Priority = "medium",
            DefaultDueInDays = 5,
            IsActive = true
        });

        await UpsertReminderRule(db, new ReminderRule
        {
            Id = "rr_deadline_7",
            Name = "7-day reminder",
            TriggerType = "deadline_approaching",
            DaysBeforeDue = 7,
            AudienceRole = "client",
            MessageTemplate = "A compliance deadline is due in 7 days.",
            IsEnabled = true
        });
        await UpsertReminderRule(db, new ReminderRule
        {
            Id = "rr_deadline_1",
            Name = "1-day reminder",
            TriggerType = "deadline_approaching",
            DaysBeforeDue = 1,
            AudienceRole = "client",
            MessageTemplate = "A compliance deadline is due tomorrow.",
            IsEnabled = true
        });

        await UpsertDeadlineRule(db, new DeadlineRule
        {
            Id = "dr_monthly_pack",
            Name = "Monthly pack due date",
            Scope = "monthly_pack",
            DueDayOfMonth = 5,
            GraceDays = 2,
            Priority = "high",
            IsEnabled = true
        });
        await UpsertDeadlineRule(db, new DeadlineRule
        {
            Id = "dr_compliance_item",
            Name = "Compliance item due date",
            Scope = "compliance_item",
            DueDayOfMonth = 25,
            GraceDays = 0,
            Priority = "critical",
            IsEnabled = true
        });

        await db.SaveChangesAsync();

        await UpsertComplianceCategory(db, new ComplianceCategory
        {
            Id = "cc_tax_compliance",
            Name = "Tax Compliance",
            Code = "TAX",
            Description = "Income tax, VAT, and tax authority filing obligations.",
            IsActive = true
        });

        await UpsertComplianceCategory(db, new ComplianceCategory
        {
            Id = "cc_cipc_compliance",
            Name = "CIPC Compliance",
            Code = "CIPC",
            Description = "Company registration, annual returns, and beneficial ownership obligations.",
            IsActive = true
        });

        await UpsertComplianceCategory(db, new ComplianceCategory
        {
            Id = "cc_payroll_compliance",
            Name = "Payroll Compliance",
            Code = "PAYROLL",
            Description = "Payroll submissions, UIF, PAYE, and employee record obligations.",
            IsActive = true
        });

        await UpsertComplianceCategory(db, new ComplianceCategory
        {
            Id = "cc_popia_compliance",
            Name = "POPIA Compliance",
            Code = "POPIA",
            Description = "Privacy controls, processing evidence, and consent obligations.",
            IsActive = true
        });
    }

    private static async Task UpsertUser(PortalDbContext db, User expected)
    {
        var byId = await db.Users.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (byId is null)
        {
            db.Users.Add(expected);
            return;
        }

        byId.FullName = expected.FullName;
        byId.Email = expected.Email;
        byId.PasswordHash = expected.PasswordHash;
        byId.Role = expected.Role;
        byId.ClientIdsJson = expected.ClientIdsJson;
        byId.UpdatedAtUtc = DateTime.UtcNow;
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
                db.RoleDefinitions.Add(new RoleDefinition
                {
                    Name = defaultRole.Name,
                    DisplayName = defaultRole.DisplayName,
                    Scope = defaultRole.Scope,
                    PermissionsJson = RolePermissions.SerializePermissions(normalizedPermissions),
                    IsSystemRole = true,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.DisplayName = defaultRole.DisplayName;
                existing.Scope = defaultRole.Scope;
                existing.PermissionsJson = RolePermissions.SerializePermissions(normalizedPermissions);
                existing.IsSystemRole = true;
                existing.IsActive = true;
                existing.UpdatedAtUtc = DateTime.UtcNow;
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
            permission.IsActive = activeSystemPermissions.Contains(permission.Key);
            permission.UpdatedAtUtc = DateTime.UtcNow;
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

        byId.Category = expected.Category;
        byId.Description = expected.Description;
        byId.IsEnabled = expected.IsEnabled;
        byId.UpdatedAtUtc = DateTime.UtcNow;
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

        existing.Status = expected.Status;
        existing.UpdatedAtUtc = DateTime.UtcNow;
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

        existing.Label = expected.Label;
        existing.IsRequired = expected.IsRequired;
        existing.Status = expected.Status;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static async Task UpsertComplianceCategory(PortalDbContext db, ComplianceCategory expected)
    {
        var existing = await db.ComplianceCategories.FirstOrDefaultAsync(x => x.Id == expected.Id || x.Code == expected.Code);
        if (existing is null)
        {
            db.ComplianceCategories.Add(expected);
            return;
        }

        existing.Name = expected.Name;
        existing.Code = expected.Code;
        existing.Description = expected.Description;
        existing.IsActive = expected.IsActive;
        existing.UpdatedAtUtc = DateTime.UtcNow;
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
                db.Permissions.Add(new Permission
                {
                    Key = permissionKey,
                    Name = permissionKey,
                    Description = $"Permission {permissionKey}",
                    IsSystemPermission = isSystemPermission,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                continue;
            }

            existing.Name = permissionKey;
            existing.IsSystemPermission = isSystemPermission || existing.IsSystemPermission;
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTime.UtcNow;
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

            db.RolePermissions.Add(new RolePermission
            {
                Id = $"rp_{Guid.NewGuid():N}",
                RoleName = roleName,
                PermissionKey = permissionKey,
                CreatedAtUtc = DateTime.UtcNow
            });
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

        existing.Name = expected.Name;
        existing.Description = expected.Description;
        existing.DocumentCategory = expected.DocumentCategory;
        existing.IsRequired = expected.IsRequired;
        existing.DefaultDueDayOfMonth = expected.DefaultDueDayOfMonth;
        existing.IsActive = expected.IsActive;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static async Task UpsertMonthlyPackTemplate(PortalDbContext db, MonthlyPackTemplate expected)
    {
        var existing = await db.MonthlyPackTemplates.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (existing is null)
        {
            db.MonthlyPackTemplates.Add(expected);
            return;
        }

        existing.Name = expected.Name;
        existing.Description = expected.Description;
        existing.AutoCreateDayOfMonth = expected.AutoCreateDayOfMonth;
        existing.IsActive = expected.IsActive;
        existing.UpdatedAtUtc = DateTime.UtcNow;
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

        existing.SortOrder = expected.SortOrder;
    }

    private static async Task UpsertRequestTemplate(PortalDbContext db, RequestTemplate expected)
    {
        var existing = await db.RequestTemplates.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (existing is null)
        {
            db.RequestTemplates.Add(expected);
            return;
        }

        existing.Name = expected.Name;
        existing.RequestType = expected.RequestType;
        existing.TitleTemplate = expected.TitleTemplate;
        existing.DescriptionTemplate = expected.DescriptionTemplate;
        existing.Priority = expected.Priority;
        existing.DefaultDueInDays = expected.DefaultDueInDays;
        existing.IsActive = expected.IsActive;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static async Task UpsertReminderRule(PortalDbContext db, ReminderRule expected)
    {
        var existing = await db.ReminderRules.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (existing is null)
        {
            db.ReminderRules.Add(expected);
            return;
        }

        existing.Name = expected.Name;
        existing.TriggerType = expected.TriggerType;
        existing.DaysBeforeDue = expected.DaysBeforeDue;
        existing.AudienceRole = expected.AudienceRole;
        existing.MessageTemplate = expected.MessageTemplate;
        existing.IsEnabled = expected.IsEnabled;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static async Task UpsertDeadlineRule(PortalDbContext db, DeadlineRule expected)
    {
        var existing = await db.DeadlineRules.FirstOrDefaultAsync(x => x.Id == expected.Id);
        if (existing is null)
        {
            db.DeadlineRules.Add(expected);
            return;
        }

        existing.Name = expected.Name;
        existing.Scope = expected.Scope;
        existing.DueDayOfMonth = expected.DueDayOfMonth;
        existing.GraceDays = expected.GraceDays;
        existing.Priority = expected.Priority;
        existing.IsEnabled = expected.IsEnabled;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }
}

