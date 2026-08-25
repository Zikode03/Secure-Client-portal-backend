using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Models;
using System.Security.Cryptography;
using System.Text;

namespace SecureClientPortal.Backend.Data;

/// <summary>
/// Seeds practical starter monthly-pack templates by business type.
/// These are starting points only: Accountant/Admin can still tailor every client's recurring profile.
/// Keeping this separate from the original SeedData avoids making the main seed routine harder to maintain.
/// </summary>
public static class BusinessMonthlyPackSeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        var requirements = new[]
        {
            Requirement("business_bank_statement", "Bank Statement", "Monthly business bank statement.", "bank_statement", true, 5),
            Requirement("sales_invoices", "Sales Invoices", "Sales invoice support for the month.", "sales_invoices", true, 5),
            Requirement("purchase_invoices", "Purchase Invoices", "Supplier and purchase invoice support.", "purchase_invoices", true, 5),
            Requirement("supplier_statements", "Supplier Statements", "Supplier statements and reconciliations.", "supplier_statements", false, 7),
            Requirement("vat_tax_documents", "VAT / Tax Documents", "VAT and tax working papers relevant to the month.", "tax_document", false, 7),
            Requirement("payroll_summary", "Payroll / PAYE", "Payroll, PAYE and employee-cost support.", "payroll_document", false, 7),
            Requirement("fuel_statements", "Fuel Statements", "Fleet fuel-card and fuel supplier statements.", "fuel_statement", true, 5),
            Requirement("vehicle_finance", "Vehicle Finance Statements", "Vehicle finance, lease and instalment support.", "vehicle_finance", false, 7),
            Requirement("toll_tracking", "Toll / Tracking Statements", "Toll, tracking and fleet operating statements.", "toll_tracking", false, 7),
            Requirement("pos_merchant", "POS / Merchant Statements", "Point-of-sale and merchant settlement statements.", "merchant_statement", true, 5),
            Requirement("stock_inventory", "Stock / Inventory Report", "Monthly stock movement or inventory valuation support.", "inventory_report", true, 7),
            Requirement("subcontractor_invoices", "Subcontractor Invoices", "Invoices and support for subcontracted work.", "subcontractor_invoices", true, 7),
            Requirement("payment_certificates", "Payment Certificates", "Project payment certificates and progress claims.", "payment_certificates", true, 7),
            Requirement("project_expenses", "Project Expense Support", "Project-specific expenses and supporting records.", "project_expenses", false, 7),
            Requirement("expense_receipts", "Expense Receipts", "Business expense receipts and supporting documents.", "receipt", false, 7),
            Requirement("booking_platform", "Booking / Platform Statements", "Booking platform and aggregator settlement statements.", "booking_statement", false, 7),
            Requirement("food_beverage_suppliers", "Food & Beverage Supplier Statements", "Key food and beverage supplier statements.", "food_supplier_statement", false, 7),
            Requirement("production_report", "Production Report", "Monthly production or manufacturing activity summary.", "production_report", false, 7),
        };

        foreach (var requirement in requirements)
        {
            await UpsertRequirement(db, requirement);
        }

        await UpsertTemplate(db, Template(
            "transport_logistics",
            "Transport & Logistics",
            "Recommended baseline for transport, logistics, courier, freight and fleet businesses.",
            requirements,
            "business_bank_statement", "sales_invoices", "purchase_invoices", "fuel_statements", "vehicle_finance", "toll_tracking", "vat_tax_documents", "payroll_summary"));

        await UpsertTemplate(db, Template(
            "retail_trading",
            "Retail & Trading",
            "Recommended baseline for retail, wholesale, ecommerce and general trading businesses.",
            requirements,
            "business_bank_statement", "sales_invoices", "purchase_invoices", "pos_merchant", "supplier_statements", "stock_inventory", "vat_tax_documents", "payroll_summary"));

        await UpsertTemplate(db, Template(
            "construction_contracting",
            "Construction & Contracting",
            "Recommended baseline for construction, engineering and contracting businesses.",
            requirements,
            "business_bank_statement", "sales_invoices", "purchase_invoices", "supplier_statements", "subcontractor_invoices", "payment_certificates", "project_expenses", "payroll_summary"));

        await UpsertTemplate(db, Template(
            "professional_services",
            "Professional Services",
            "Recommended baseline for consulting, legal, technology and other service businesses.",
            requirements,
            "business_bank_statement", "sales_invoices", "expense_receipts", "vat_tax_documents", "payroll_summary"));

        await UpsertTemplate(db, Template(
            "manufacturing",
            "Manufacturing & Production",
            "Recommended baseline for manufacturing, production and factory-based businesses.",
            requirements,
            "business_bank_statement", "sales_invoices", "purchase_invoices", "supplier_statements", "stock_inventory", "production_report", "payroll_summary", "vat_tax_documents"));

        await UpsertTemplate(db, Template(
            "hospitality",
            "Hospitality & Food Service",
            "Recommended baseline for hotels, restaurants, catering and hospitality businesses.",
            requirements,
            "business_bank_statement", "sales_invoices", "purchase_invoices", "pos_merchant", "food_beverage_suppliers", "booking_platform", "payroll_summary", "vat_tax_documents"));

        await db.SaveChangesAsync();
    }

    private static RequiredDocumentTemplate Requirement(
        string key,
        string name,
        string description,
        string category,
        bool required,
        int? dueDay) =>
        RequiredDocumentTemplate.Create(Id($"business_requirement:{key}"), name, description, category, required, dueDay, true);

    private static (MonthlyPackTemplate template, List<MonthlyPackTemplateItem> items) Template(
        string key,
        string name,
        string description,
        IReadOnlyCollection<RequiredDocumentTemplate> requirements,
        params string[] requirementKeys)
    {
        var templateId = Id($"business_template:{key}");
        var template = MonthlyPackTemplate.Create(templateId, name, description, 1, true);
        var items = new List<MonthlyPackTemplateItem>();

        for (var index = 0; index < requirementKeys.Length; index++)
        {
            var requirementId = Id($"business_requirement:{requirementKeys[index]}");
            if (requirements.All(x => x.Id != requirementId)) continue;
            items.Add(MonthlyPackTemplateItem.Create(
                Id($"business_template_item:{key}:{requirementKeys[index]}"),
                templateId,
                requirementId,
                index + 1));
        }

        return (template, items);
    }

    private static async Task UpsertRequirement(PortalDbContext db, RequiredDocumentTemplate requirement)
    {
        if (!await db.RequiredDocumentTemplates.AnyAsync(x => x.Id == requirement.Id))
        {
            db.RequiredDocumentTemplates.Add(requirement);
        }
    }

    private static async Task UpsertTemplate(
        PortalDbContext db,
        (MonthlyPackTemplate template, List<MonthlyPackTemplateItem> items) definition)
    {
        if (!await db.MonthlyPackTemplates.AnyAsync(x => x.Id == definition.template.Id))
        {
            db.MonthlyPackTemplates.Add(definition.template);
        }

        foreach (var item in definition.items)
        {
            if (!await db.MonthlyPackTemplateItems.AnyAsync(x => x.Id == item.Id))
            {
                db.MonthlyPackTemplateItems.Add(item);
            }
        }
    }

    // Stable deterministic ids keep seed data idempotent across environments.
    private static Guid Id(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"secure-client-portal:{value}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
