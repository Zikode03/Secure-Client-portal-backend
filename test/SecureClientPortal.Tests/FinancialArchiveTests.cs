using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureClientPortal.Backend.Api.Modules.Documents;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;
namespace SecureClientPortal.Backend.Tests;
public sealed class FinancialArchiveTests {
 private static ClaimsPrincipal Actor(string role)=>new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString()),new(ClaimTypes.Role,role),new("role_scope",role)],"test"));
 private static PortalDbContext Db()=>new(new DbContextOptionsBuilder<PortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
 private static Document Seed(PortalDbContext db,bool linked=false){var client=Client.Create(Guid.NewGuid(),"Archive client","Company","Contact","client@example.invalid",ClientStatus.Active);db.Clients.Add(client);var pack=MonthlyPack.Create(Guid.NewGuid(),client.Id,2005,3);db.MonthlyPacks.Add(pack);var doc=Document.CreateUploaded(Guid.NewGuid(),client.Id,pack.Id,"Record.pdf","general",linked?Guid.NewGuid():null,"application/pdf",20,"private",Guid.NewGuid());db.Documents.Add(doc);db.SaveChanges();return doc;}
 private static DocumentArchiveController Controller(PortalDbContext db,ClaimsPrincipal actor)=>new(db){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext{User=actor}}};
 [Fact] public async Task PermanentClassificationIsAuditedAndSurvivesReloadWithoutReviewOrPeriodMutation(){await using var db=Db();var doc=Seed(db);var actor=Actor("admin");var controller=Controller(db,actor);
  var response=await controller.Place(doc.Id,new("permanent",null,null,1),TestContext.Current.CancellationToken);Assert.IsType<OkObjectResult>(response.Result);Assert.Equal("uploaded",doc.Status);Assert.Null(doc.MonthlyPackId);Assert.Empty(db.ReviewDecisions);Assert.Contains(db.AuditLogs,a=>a.Action=="documents.archive_classified");
  db.ChangeTracker.Clear();var query=new DocumentQueryService(db,db,null!);var reloaded=Assert.Single(await query.GetAllAsync(actor,TestContext.Current.CancellationToken));Assert.Equal("permanent",reloaded.ArchiveScope);Assert.Null(reloaded.ArchiveYear);Assert.Null(reloaded.ArchiveMonth);
 }
 [Fact] public async Task ForeignAccountantCannotClassifyDocuments(){await using var db=Db();var doc=Seed(db);var response=await Controller(db,Actor("accountant")).Place(doc.Id,new("permanent",null,null,1),TestContext.Current.CancellationToken);Assert.IsType<ForbidResult>(response.Result);Assert.Empty(db.AuditLogs);}
 [Fact] public async Task ChecklistLinksAndStaleVersionsCannotBeReorganised(){await using var db=Db();var doc=Seed(db,true);var controller=Controller(db,Actor("admin"));Assert.IsType<BadRequestObjectResult>((await controller.Place(doc.Id,new("permanent",null,null,1),TestContext.Current.CancellationToken)).Result);Assert.IsType<ConflictObjectResult>((await controller.Place(doc.Id,new("monthly",2005,3,2),TestContext.Current.CancellationToken)).Result);Assert.Empty(db.AuditLogs);}
 [Fact] public async Task NonMonthlyRecordsRemainInTheReviewQueue(){await using var db=Db();var doc=Seed(db);doc.SetArchiveMonthlyPack(null);doc.MarkUnderReview();await db.SaveChangesAsync(TestContext.Current.CancellationToken);
  var queue=new SecureClientPortal.Backend.Infrastructure.Modules.ReviewQueue.ReviewQueueService(db);var result=await queue.GetPendingAsync(Actor("admin"),null,TestContext.Current.CancellationToken);var row=Assert.Single(result.items);Assert.Null(row.MonthlyPackId);Assert.Null(row.Year);Assert.Null(row.Month);
 }
 [Theory][InlineData("annual",null,null)][InlineData("monthly",2005,13)][InlineData("unsupported",2005,1)]
 public async Task InvalidClassificationsDoNotWriteAudit(string scope,int? year,int? month){await using var db=Db();var doc=Seed(db);var result=await Controller(db,Actor("admin")).Place(doc.Id,new(scope,year,month,1),TestContext.Current.CancellationToken);Assert.IsType<BadRequestObjectResult>(result.Result);Assert.Empty(db.AuditLogs);}
}
