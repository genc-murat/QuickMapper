namespace QuickMapper.Extensions;

/// <summary>
/// Provides extension methods for object-to-object mapping functionalities.
/// </summary>
public static class ObjectMappingExtensions
{
    /// <summary>
    /// Maps the properties of the source object to a new object of type T.
    /// </summary>
    /// <typeparam name="T">The type to map to.</typeparam>
    /// <param name="source">The source object.</param>
    /// <returns>A new object of type T with mapped properties.</returns>
    public static T MapTo<T>(this object source) where T : new()
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var sourceType = source.GetType();

        var mapMethod = typeof(QuickMapper).GetMethod("Map").MakeGenericMethod(new Type[] { sourceType, typeof(T) });
        return (T)mapMethod.Invoke(null, new object[] { source });
    }

    /// <summary>
    /// Maps the properties of the source object to a new object of type T with additional options.
    /// </summary>
    /// <typeparam name="T">The type to map to.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="skipNulls">Whether to skip null values during mapping.</param>
    /// <param name="ignoreMissing">Whether to ignore missing properties during mapping.</param>
    /// <param name="performanceLogging">Whether to enable performance logging.</param>
    /// <returns>A new object of type T with mapped properties.</returns>
    public static T MapTo<T>(this object source, bool skipNulls, bool ignoreMissing, bool performanceLogging = false) where T : new()
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return QuickMapper.Map<object, T>(source, skipNulls, ignoreMissing, performanceLogging);
    }

    /// <summary>
    /// Maps the properties of the source object to a new object of type T and allows additional configuration.
    /// </summary>
    /// <typeparam name="T">The type to map to.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="configure">An action to configure the target object.</param>
    /// <returns>A new object of type T with mapped and configured properties.</returns>
    public static T MapTo<T>(this object source, Action<T> configure) where T : new()
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return QuickMapper.Map<object, T>(source, configure);
    }

    /// <summary>
    /// Maps the properties of the source object to a new object of type T with additional configuration and options.
    /// </summary>
    /// <typeparam name="T">The type to map to.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="configure">An action to configure the target object.</param>
    /// <param name="skipNulls">Whether to skip null values during mapping.</param>
    /// <param name="ignoreMissing">Whether to ignore missing properties during mapping.</param>
    /// <param name="performanceLogging">Whether to enable performance logging.</param>
    /// <returns>A new object of type T with mapped and configured properties.</returns>
    public static T MapTo<T>(this object source, Action<T> configure, bool skipNulls, bool ignoreMissing, bool performanceLogging = false) where T : new()
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return QuickMapper.Map<object, T>(source, configure, skipNulls, ignoreMissing, performanceLogging);
    }
}
