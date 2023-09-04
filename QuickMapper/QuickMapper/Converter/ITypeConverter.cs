namespace QuickMapper;

/// <summary>
/// Defines the contract for type converters that support converting between different types.
/// Implement this interface to provide custom type conversion logic.
/// </summary>
public interface ITypeConverter
{
    /// <summary>
    /// Determines whether the converter can convert between the specified source and target types.
    /// </summary>
    /// <param name="sourceType">The type of the source object.</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <returns>
    /// True if the converter can perform the conversion; otherwise, false.
    /// </returns>
    bool CanConvert(Type sourceType, Type targetType);

    /// <summary>
    /// Converts the given source object to the specified target type.
    /// </summary>
    /// <param name="source">The source object to convert.</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <returns>The converted object.</returns>
    object Convert(object source, Type targetType);
}
