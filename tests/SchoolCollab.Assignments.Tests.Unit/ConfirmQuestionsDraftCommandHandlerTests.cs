using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — <see cref="ConfirmQuestionsDraftCommandHandler"/>
/// atomically REPLACES all existing questions with the staged draft and clears the
/// blob. The handler is unit-tested against a mocked <see cref="IAssignmentRepository"/>
/// (the repo/EF boundary is the in-memory suite's InMemory-concurrency quirk zone);
/// a missing or corrupt blob throws the typed
/// <see cref="InvalidQuestionsDraftException"/> (defensive — staging validates first).
/// </summary>
[TestClass]
public class ConfirmQuestionsDraftCommandHandlerTests
{
    private static HybridCache NewCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private static ConfirmQuestionsDraftCommandHandler NewHandler(IAssignmentRepository repo, HybridCache cache) =>
        new(repo, cache, NullLogger<ConfirmQuestionsDraftCommandHandler>.Instance);

    private static Assignment NewDraftAssignment(bool withExistingQuestion = false)
    {
        var assignment = Assignment.Create(
                "Title", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
                TargetAudienceType.AllStudents, Guid.NewGuid(), null, null, null);
        if (withExistingQuestion)
        {
            assignment.AddQuestion("Existing question", QuestionType.ShortAnswer, 1, null);
        }
        return assignment;
    }

    private static NewQuestionDto ShortAnswer(string text, int displayOrder) =>
        new(text, QuestionTypeDto.ShortAnswer, displayOrder, null, "draft answer");

    private static string ValidDraftBlob(params (string Text, int Order)[] entries)
    {
        var list = entries.Select(e => ShortAnswer(e.Text, e.Order)).ToList();
        return JsonSerializer.Serialize(new List<NewQuestionDto>(list), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static Mock<IAssignmentRepository> MockRepo(Assignment staged)
    {
        var repo = new Mock<IAssignmentRepository>();
        repo.Setup(r => r.GetAsync(staged.Id, It.IsAny<CancellationToken>())).ReturnsAsync(staged);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Assignment>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }

    [TestMethod]
    public async Task ReplacesAllExistingQuestions()
    {
        var cache = NewCache();
        var assignment = NewDraftAssignment(withExistingQuestion: true);
        assignment.StageQuestionsDraft(ValidDraftBlob(("Draft 1", 0), ("Draft 2", 1)));
        var repo = MockRepo(assignment);
        var handler = NewHandler(repo.Object, cache);

        await handler.HandleAsync(new ConfirmQuestionsDraftCommand(assignment.Id));

        assignment.Questions.Should().HaveCount(2, "confirm REPLACES the existing single question");
        assignment.Questions.Select(q => q.QuestionText).Should().Contain(new[] { "Draft 1", "Draft 2" });
        assignment.QuestionsDraftJson.Should().BeNull("the blob is cleared on confirm");
        repo.Verify(r => r.UpdateAsync(assignment, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ClearsBlobOnConfirm()
    {
        var cache = NewCache();
        var assignment = NewDraftAssignment();
        assignment.StageQuestionsDraft(ValidDraftBlob(("Draft", 0)));
        var repo = MockRepo(assignment);
        var handler = NewHandler(repo.Object, cache);

        await handler.HandleAsync(new ConfirmQuestionsDraftCommand(assignment.Id));

        assignment.QuestionsDraftJson.Should().BeNull();
        assignment.Questions.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Throws_OnMissingBlob()
    {
        var cache = NewCache();
        var assignment = NewDraftAssignment();
        var repo = MockRepo(assignment);
        var handler = NewHandler(repo.Object, cache);

        var act = async () => await handler.HandleAsync(new ConfirmQuestionsDraftCommand(assignment.Id));

        await act.Should().ThrowAsync<InvalidQuestionsDraftException>();
    }

    [TestMethod]
    public async Task Throws_OnCorruptBlob()
    {
        var cache = NewCache();
        var assignment = NewDraftAssignment();
        assignment.StageQuestionsDraft("this is not json");
        var repo = MockRepo(assignment);
        var handler = NewHandler(repo.Object, cache);

        var act = async () => await handler.HandleAsync(new ConfirmQuestionsDraftCommand(assignment.Id));

        await act.Should().ThrowAsync<InvalidQuestionsDraftException>();
    }
}
