namespace QuickMapper;

/// <summary>
/// Indicates that the mapping for the decorated property should be skipped if its value is null.
/// This attribute can be applied to properties that should not be mapped when their value is null.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class SkipIfNullAttribute : Attribute
{
}
