#nullable enable
using FluentAssertions;
using FluentIcons.Common;
using Leaf.Models;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Pins the extension → <see cref="Symbol"/> mapping used by the conflict
/// file tree. Keeps the icon vocabulary consistent across file types and
/// prevents accidental drift when a future extension is added.
/// </summary>
public class FileTypeIconResolverTests
{
    [Theory]
    [InlineData("Foo.cs", Symbol.Code)]
    [InlineData("foo.FS", Symbol.Code)]
    [InlineData("app.ts", Symbol.Code)]
    [InlineData("view.xaml", Symbol.Code)]
    [InlineData("script.ps1", Symbol.Code)]
    [InlineData("page.html", Symbol.Code)]
    [InlineData("main.cpp", Symbol.Code)]
    [InlineData("config.json", Symbol.Braces)]
    [InlineData("schema.yml", Symbol.Braces)]
    [InlineData("db.sql", Symbol.Braces)]
    [InlineData("logo.png", Symbol.Image)]
    [InlineData("icon.svg", Symbol.Image)]
    [InlineData("photo.JPG", Symbol.Image)]
    [InlineData("README.md", Symbol.Document)]
    [InlineData("notes.txt", Symbol.Document)]
    [InlineData("NOEXT", Symbol.Document)]
    [InlineData("unknown.zzz", Symbol.Document)]
    public void ResolveForFile_MapsExtensionToExpectedSymbol(string fileName, Symbol expected)
    {
        FileTypeIconResolver.ResolveForFile(fileName).Should().Be(expected);
    }

    [Fact]
    public void ResolveForFile_NullOrEmpty_Throws()
    {
        FluentActions.Invoking(() => FileTypeIconResolver.ResolveForFile(null!))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => FileTypeIconResolver.ResolveForFile(string.Empty))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConflictTreeNode_FileLeaf_ReceivesResolvedIconSymbol()
    {
        var node = ConflictTreeNode.File(new ConflictInfo { FilePath = "src/App.cs" });
        node.IconSymbol.Should().Be(Symbol.Code);
    }

    [Fact]
    public void ConflictTreeNode_FolderNode_UsesFolderSymbol()
    {
        var child = ConflictTreeNode.File(new ConflictInfo { FilePath = "src/App.cs" });
        var folder = ConflictTreeNode.Folder("src", "src", new[] { child });
        folder.IconSymbol.Should().Be(Symbol.Folder);
    }
}
