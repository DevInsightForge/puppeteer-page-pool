namespace PuppeteerPagePool.Exceptions;

/// <summary>
/// Base exception for all PuppeteerPagePool exceptions.
/// </summary>
public abstract class PagePoolException(string message, string? poolName = null, Guid correlationId = default, Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>
    /// Gets the name of the pool associated with this exception.
    /// </summary>
    public string? PoolName { get; } = poolName;

    /// <summary>
    /// Gets the correlation ID for the operation that failed.
    /// </summary>
    public Guid CorrelationId { get; } = correlationId == default ? Guid.NewGuid() : correlationId;
}

/// <summary>
/// Thrown when no page becomes available before the configured acquire timeout expires.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PagePoolAcquireTimeoutException"/> class.
/// </remarks>
public sealed class PagePoolAcquireTimeoutException(
    TimeSpan timeout,
    int availablePages = 0,
    int leasedPages = 0,
    string? poolName = null,
    Guid correlationId = default) : PagePoolException($"Timed out acquiring a page lease after {timeout}.", poolName, correlationId)
{
    /// <summary>
    /// Gets the timeout duration that was exceeded.
    /// </summary>
    public TimeSpan Timeout { get; } = timeout;

    /// <summary>
    /// Gets the number of pages that were available at the time of timeout.
    /// </summary>
    public int AvailablePages { get; } = availablePages;

    /// <summary>
    /// Gets the number of pages that were leased at the time of timeout.
    /// </summary>
    public int LeasedPages { get; } = leasedPages;
}

/// <summary>
/// Thrown when work is requested from a disposed pool or from a pool that no longer accepts leases.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PagePoolDisposedException"/> class.
/// </remarks>
public sealed class PagePoolDisposedException(string? poolName = null, Guid correlationId = default) : PagePoolException("The page pool has been disposed and cannot accept new operations.", poolName, correlationId)
{
}

/// <summary>
/// Thrown when browser startup, connection, validation, or rebuild fails.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PagePoolUnavailableException"/> class.
/// </remarks>
public sealed class PagePoolUnavailableException(
    string message,
    FailureType failureType = FailureType.Unknown,
    string? poolName = null,
    Guid correlationId = default,
    Exception? innerException = null) : PagePoolException(message, poolName, correlationId, innerException)
{
    /// <summary>
    /// Gets the type of failure.
    /// </summary>
    public FailureType FailureType { get; } = failureType;
}

/// <summary>
/// Thrown when a page operation fails during execution.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PageOperationException"/> class.
/// </remarks>
public sealed class PageOperationException(
    string operation,
    Exception innerException,
    int generation = 0,
    int useCount = 0,
    string? poolName = null,
    Guid correlationId = default) : PagePoolException($"Page operation '{operation}' failed.", poolName, correlationId, innerException)
{
    /// <summary>
    /// Gets the type of operation that failed.
    /// </summary>
    public string Operation { get; } = operation;

    /// <summary>
    /// Gets the generation of the page that failed.
    /// </summary>
    public int Generation { get; } = generation;

    /// <summary>
    /// Gets the use count of the page that failed.
    /// </summary>
    public int UseCount { get; } = useCount;
}

/// <summary>
/// Thrown when the circuit breaker is open and operations are blocked.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PagePoolCircuitOpenException"/> class.
/// </remarks>
public sealed class PagePoolCircuitOpenException(
    DateTime retryAfter,
    string? poolName = null,
    Guid correlationId = default) : PagePoolException($"The page pool circuit breaker is open. Retry after {retryAfter}.", poolName, correlationId)
{
    /// <summary>
    /// Gets the time when the circuit breaker will transition to half-open state.
    /// </summary>
    public DateTime RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Specifies the type of failure for <see cref="PagePoolUnavailableException"/>.
/// </summary>
public enum FailureType
{
    /// <summary>
    /// Unknown failure type.
    /// </summary>
    Unknown,

    /// <summary>
    /// Browser launch failed.
    /// </summary>
    LaunchFailed,

    /// <summary>
    /// Browser connection failed.
    /// </summary>
    ConnectionFailed,

    /// <summary>
    /// Browser validation failed.
    /// </summary>
    ValidationFailed,

    /// <summary>
    /// Browser rebuild failed.
    /// </summary>
    RebuildFailed,

    /// <summary>
    /// Page creation failed.
    /// </summary>
    PageCreationFailed,

    /// <summary>
    /// Browser executable not found.
    /// </summary>
    ExecutableNotFound,

    /// <summary>
    /// Browser executable validation failed.
    /// </summary>
    ExecutableValidationFailed
}
