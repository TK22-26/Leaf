using Xunit;

// WPF tests share the process-global Application.Current, whose
// ResourceDictionary mutates internal (non-concurrent) dictionaries even
// on READ (GetOrCreateWeakReferenceList registers owners during resource
// lookup). xunit's default class-parallelism runs [StaFact] classes on
// separate STA threads concurrently, and two simultaneous resource
// lookups corrupt that dictionary — the source of the intermittent
// XamlParseException / NullReferenceException flakes across the
// Controls.Merge test classes. WPF offers no thread-safe mode for this,
// so the assembly runs sequentially. Measured cost: a few seconds on the
// full suite; the win is deterministic UI tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
