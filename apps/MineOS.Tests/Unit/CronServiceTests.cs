using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Moq;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class CronServiceTests
{
    private readonly Mock<IRepository<CronJob>> _repo = new();
    private readonly CronService _service;

    public CronServiceTests()
    {
        _service = new CronService(_repo.Object, Mock.Of<ILogger<CronService>>());
    }

    [Fact]
    public async Task ListAsync_Returns_Jobs_For_Server()
    {
        var jobs = new List<CronJob>
        {
            new() { Id = 1, ServerName = "test", CronExpression = "0 * * * *", Action = "restart" },
            new() { Id = 2, ServerName = "test", CronExpression = "0 0 * * *", Action = "backup" }
        };
        _repo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<CronJob, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        var result = (await _service.ListAsync("test", CancellationToken.None)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, dto => Assert.False(string.IsNullOrEmpty(dto.Hash)));
    }

    [Fact]
    public async Task CreateAsync_Adds_Entity_And_Returns_Dto()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<CronJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CreateCronRequest("*/5 * * * *", "backup", "Daily backup");
        var result = await _service.CreateAsync("myserver", request, CancellationToken.None);

        Assert.Equal("*/5 * * * *", result.Source);
        Assert.Equal("backup", result.Action);
        Assert.Equal("Daily backup", result.Msg);
        Assert.True(result.Enabled);
        Assert.False(string.IsNullOrEmpty(result.Hash));

        _repo.Verify(r => r.AddAsync(It.Is<CronJob>(j =>
            j.ServerName == "myserver" &&
            j.CronExpression == "*/5 * * * *" &&
            j.Action == "backup"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Null_When_Hash_Not_Found()
    {
        _repo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<CronJob, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CronJob>());

        var result = await _service.UpdateAsync("test", "nonexistent", new UpdateCronRequest(false), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_Toggles_Enabled()
    {
        var job = new CronJob { Id = 1, ServerName = "test", CronExpression = "0 * * * *", Action = "restart", Enabled = true };
        _repo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<CronJob, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CronJob> { job });
        _repo.Setup(r => r.UpdateAsync(It.IsAny<CronJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hash = CronService.ComputeHash(job);
        var result = await _service.UpdateAsync("test", hash, new UpdateCronRequest(false), CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.Enabled);
    }

    [Fact]
    public async Task DeleteAsync_Returns_False_When_Not_Found()
    {
        _repo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<CronJob, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CronJob>());

        var result = await _service.DeleteAsync("test", "nonexistent", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_Removes_Entity_When_Found()
    {
        var job = new CronJob { Id = 1, ServerName = "test", CronExpression = "0 * * * *", Action = "restart" };
        _repo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<CronJob, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CronJob> { job });
        _repo.Setup(r => r.RemoveAsync(It.IsAny<CronJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hash = CronService.ComputeHash(job);
        var result = await _service.DeleteAsync("test", hash, CancellationToken.None);

        Assert.True(result);
        _repo.Verify(r => r.RemoveAsync(job, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ComputeHash_Is_Deterministic()
    {
        var job = new CronJob { Id = 1, ServerName = "test", CronExpression = "0 * * * *", Action = "restart" };
        var hash1 = CronService.ComputeHash(job);
        var hash2 = CronService.ComputeHash(job);

        Assert.Equal(hash1, hash2);
        Assert.Equal(12, hash1.Length);
    }

    [Fact]
    public void ComputeHash_Differs_For_Different_Jobs()
    {
        var job1 = new CronJob { Id = 1, ServerName = "test", CronExpression = "0 * * * *", Action = "restart" };
        var job2 = new CronJob { Id = 2, ServerName = "test", CronExpression = "0 * * * *", Action = "restart" };

        Assert.NotEqual(CronService.ComputeHash(job1), CronService.ComputeHash(job2));
    }
}
