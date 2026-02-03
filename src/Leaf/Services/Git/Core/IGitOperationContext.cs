namespace Leaf.Services.Git.Core;

/// <summary>
/// Interface for shared dependencies used by all Git operation classes.
/// Provides access to the command runner and helper utilities.
/// </summary>
internal interface IGitOperationContext
{
    /// <summary>
    /// The command runner for executing git CLI commands.
    /// </summary>
    IGitCommandRunner CommandRunner { get; }

    /// <summary>
    /// Helper utilities for common git CLI operations.
    /// </summary>
    GitCliHelpers CliHelpers { get; }

    /// <summary>
    /// Parser for git command output.
    /// </summary>
    IGitOutputParser OutputParser { get; }

    /// <summary>
    /// Mapper for git CLI errors to user-friendly messages.
    /// </summary>
    IGitErrorMapper ErrorMapper { get; }
}
