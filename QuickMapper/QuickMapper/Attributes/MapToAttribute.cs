namespace QuickMapper;

/// <summary>
/// Specifies the target property name to which the decorated property should be mapped.
/// This attribute can be applied to properties that need to be mapped to a property with a different name.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class MapToAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the target property to which the decorated property should be mapped.
    /// </summary>
    public string TargetPropertyName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapToAttribute"/> class.
    /// </summary>
    /// <param name="targetPropertyName">The name of the target property to which the decorated property should be mapped.</param>
    public MapToAttribute(string targetPropertyName)
    {
        TargetPropertyName = targetPropertyName;
    }
}
