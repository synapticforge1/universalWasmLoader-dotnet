namespace UniversalWasmLoader;

/// <summary>
/// Options for <see cref="Loader.ImportAsync"/> (SPEC 4.0.0 §1, §11): the time limit for each
/// <c>http(s)</c> fetch and a cancellation token. Local files are read without a limit.
/// </summary>
public sealed class LoadOptions
{
    /// <summary>The default time limit for each http(s) fetch: 30 seconds (SPEC §11).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _timeout = DefaultTimeout;

    /// <summary>
    /// Time limit for each http(s) fetch, from the request to the last byte of the body. Default
    /// <see cref="DefaultTimeout"/>; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> disables
    /// it. Zero, a negative value, or more than <see cref="int.MaxValue"/> milliseconds is refused.
    /// </summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        init
        {
            if (value != System.Threading.Timeout.InfiniteTimeSpan
                && (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "LoadOptions.Timeout must be positive (at most int.MaxValue ms) or Timeout.InfiniteTimeSpan");
            _timeout = value;
        }
    }

    /// <summary>Cancels the load; an already-cancelled token fails it before any fetch (SPEC §11).</summary>
    public CancellationToken CancellationToken { get; init; }
}
