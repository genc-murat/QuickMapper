namespace QuickMapper;

/// <summary>
/// Specifies a default value for a property during mapping.
/// This attribute can be applied to properties to set a default value when mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class DefaultValueAttribute : Attribute
{
    /// <summary>
    /// Gets the default value for the property.
    /// </summary>
    public object DefaultValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultValueAttribute"/> class.
    /// </summary>
    /// <param name="defaultValue">The default value to set for the property.</param>
    public DefaultValueAttribute(object defaultValue)
    {
        DefaultValue = defaultValue;
    }
}
