namespace QuickMapper;

/// <summary>
/// Defines the contract for custom property converters.
/// Implement this interface to provide custom conversion logic for property mapping.
/// </summary>
public interface ICustomPropertyConverter
{
    /// <summary>
    /// Converts the source property value to another type or format.
    /// </summary>
    /// <param name="sourcePropertyValue">The source property value that needs to be converted.</param>
    /// <returns>The converted value.</returns>
    object Convert(object sourcePropertyValue);
}
