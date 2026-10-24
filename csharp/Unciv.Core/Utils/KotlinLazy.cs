namespace Unciv.Utils;

// Port of Kotlin's `by lazy { }` (default mode SYNCHRONIZED, kotlin.SynchronizedLazyImpl).
// System.Lazy<T> is banned (BannedSymbols.txt): in its default mode it caches an exception thrown by the
// factory and rethrows it forever. Kotlin does not: a failed initializer leaves the lazy uninitialized and
// the next access runs it again. Like Kotlin, the factory is released after a successful run, the lock
// is reentrant (recursive access is the caller's bug, as in Kotlin), and a fast volatile check avoids locking once initialized.
public sealed class KotlinLazy<T>
{
    private readonly object syncRoot = new();
    private Func<T>? initializer;
    private volatile bool initialized;
    private T result = default!; // JUSTIFY: T119 unconstrained T, read only when initialized

    public KotlinLazy(Func<T> initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        this.initializer = initializer;
    }

    public bool IsValueCreated => initialized;

    public T Value
    {
        get
        {
            if (initialized) return result;
            lock (syncRoot)
            {
                if (initialized) return result;
                result = initializer!(); // an exception propagates and nothing is cached; JUSTIFY: T119 initializer is non-null until initialized
                initialized = true;      // publish (volatile write) after `result` is set
                initializer = null;
                return result;
            }
        }
    }

    public override string ToString() => IsValueCreated ? result?.ToString() ?? "" : "Lazy value not initialized yet.";
}
