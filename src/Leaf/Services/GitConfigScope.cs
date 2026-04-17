namespace Leaf.Services;

/// <summary>
/// Scope for git-config reads and writes. Mirrors <c>git config</c>'s own
/// scope flags. <see cref="Local"/> targets <c>.git/config</c>;
/// <see cref="Global"/> targets <c>~/.gitconfig</c>.
/// </summary>
public enum GitConfigScope
{
    Local,
    Global
}
