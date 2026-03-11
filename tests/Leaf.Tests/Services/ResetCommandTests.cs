using FluentAssertions;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

public class ResetCommandTests
{
    [Fact]
    public void ResetCommand_Soft_EmitsCorrectArgs()
    {
        var command = new ResetCommand { Target = "abc1234", Mode = GitResetMode.Soft };
        var args = command.ToArguments();
        args.Should().Equal("reset", "--soft", "abc1234");
    }

    [Fact]
    public void ResetCommand_Mixed_EmitsCorrectArgs()
    {
        var command = new ResetCommand { Target = "abc1234", Mode = GitResetMode.Mixed };
        var args = command.ToArguments();
        args.Should().Equal("reset", "abc1234");
    }

    [Fact]
    public void ResetCommand_Hard_EmitsCorrectArgs()
    {
        var command = new ResetCommand { Target = "abc1234", Mode = GitResetMode.Hard };
        var args = command.ToArguments();
        args.Should().Equal("reset", "--hard", "abc1234");
    }
}
