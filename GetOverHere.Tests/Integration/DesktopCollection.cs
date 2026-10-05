namespace GetOverHere.Tests.Integration;

/// <summary>
/// Tests on the real Windows desktop: they briefly move the cursor and change the active window.
/// They never run in parallel. To exclude them: dotnet test --filter Category!=Integration
/// </summary>
[CollectionDefinition(nameof(DesktopCollection), DisableParallelization = true)]
public class DesktopCollection;
