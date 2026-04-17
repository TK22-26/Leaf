namespace Leaf.Composition;

/// <summary>
/// Scoped context that carries the repository path into the per-repo DI
/// scope. <see cref="Leaf.Services.IRepositorySession"/> is registered as
/// scoped with a factory delegate that reads this context — which means
/// the caller that opens the scope has to set <see cref="Path"/> before
/// anything resolves the session.
/// <para>
/// MainViewModel is the only legitimate writer: on repo switch it creates
/// a fresh scope, stashes the path here, then resolves the session (which
/// eagerly constructs it so path-validation errors surface immediately
/// rather than on the first git operation).
/// </para>
/// </summary>
public sealed class RepositoryScopeContext
{
    /// <summary>
    /// Absolute path to the repository this scope is bound to. Null only
    /// before MainViewModel sets it — resolving <c>IRepositorySession</c>
    /// in that state is a bug.
    /// </summary>
    public string? Path { get; set; }
}
