namespace QuickMapper;

/// <summary>
/// Specifies a custom converter for a property during mapping.
/// This attribute should be applied to properties that need custom conversion logic.
/// </summary>
/// <remarks>
/// The converter type provided must implement the ICustomPropertyConverter interface.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class CustomConverterAttribute : Attribute
{
    /// <summary>
    /// Gets the type of the custom converter.
    /// </summary>
    public Type ConverterType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomConverterAttribute"/> class.
    /// </summary>
    /// <param name="converterType">The type of the custom converter. Must implement ICustomPropertyConverter.</param>
    /// <exception cref="ArgumentException">Thrown when the provided type doesn't implement ICustomPropertyConverter.</exception>
    public CustomConverterAttribute(Type converterType)
    {
        if (!typeof(ICustomPropertyConverter).IsAssignableFrom(converterType))
        {
            throw new ArgumentException("Converter type must implement ICustomPropertyConverter", nameof(converterType));
        }

        ConverterType = converterType;
    }
}
