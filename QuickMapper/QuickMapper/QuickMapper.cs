using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace QuickMapper;

/// <summary>
/// Provides a mechanism for mapping properties between objects of different types.
/// </summary>
public static class QuickMapper
{
    private static readonly Dictionary<string, Func<object, object>> CustomMappers = new Dictionary<string, Func<object, object>>();
    private static readonly Dictionary<string, (PropertyInfo SourceProperty, PropertyInfo TargetProperty)[]> CachedMappings = new Dictionary<string, (PropertyInfo SourceProperty, PropertyInfo TargetProperty)[]>();

    private static readonly object CustomMapperLock = new object();
    private static readonly object CachedMappingLock = new object();
    private static readonly List<ITypeConverter> TypeConverters = new List<ITypeConverter>();

    private static readonly Dictionary<string, Delegate> CachedDelegates = new Dictionary<string, Delegate>();

    /// <summary>
    /// Registers a custom type converter to the mapping engine.
    /// </summary>
    /// <param name="converter">The type converter to register.</param>
    public static void RegisterTypeConverter(ITypeConverter converter)
    {
        TypeConverters.Add(converter);
    }

    /// <summary>
    /// Registers a custom mapping function for mapping objects from type TSource to type TTarget.
    /// </summary>
    /// <param name="mapper">The mapping function.</param>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TTarget">The target type.</typeparam>
    public static void RegisterCustomMapper<TSource, TTarget>(Func<TSource, TTarget> mapper)
    {
        lock (CustomMapperLock)
        {
            string key = $"{typeof(TSource).FullName}->{typeof(TTarget).FullName}";
            CustomMappers[key] = x => mapper((TSource)x);
        }
    }

    /// <summary>
    /// Gets the cached property mappings between a source type and a target type.
    /// If the cache doesn't contain the mappings, it calculates them and then stores them in the cache.
    /// </summary>
    /// <param name="sourceType">The type of the source object.</param>
    /// <param name="targetType">The type of the target object.</param>
    /// <returns>An array of tuple containing PropertyInfo of matching properties in source and target types.</returns>
    private static (PropertyInfo SourceProperty, PropertyInfo TargetProperty)[] GetCachedMappings(Type sourceType, Type targetType)
    {
        lock (CachedMappingLock)
        {
            string key = $"{sourceType.FullName}->{targetType.FullName}";
            if (CachedMappings.TryGetValue(key, out var mappings))
            {
                return mappings;
            }

            List<(PropertyInfo SourceProperty, PropertyInfo TargetProperty)> newMappings = new List<(PropertyInfo SourceProperty, PropertyInfo TargetProperty)>();

            var sourceProperties = sourceType.GetProperties().ToDictionary(p => p.Name, p => p);
            var targetProperties = targetType.GetProperties().ToDictionary(p => p.Name, p => p);

            foreach (var sourceProperty in sourceProperties)
            {
                if (targetProperties.TryGetValue(sourceProperty.Key, out var targetProperty))
                {
                    if (sourceProperty.Value.PropertyType == targetProperty.PropertyType)
                    {
                        newMappings.Add((sourceProperty.Value, targetProperty));
                    }
                    else
                    {
                        var attribute = sourceProperty.Value.GetCustomAttribute<MapToAttribute>();
                        if (attribute != null && targetProperties.TryGetValue(attribute.TargetPropertyName, out targetProperty))
                        {
                            newMappings.Add((sourceProperty.Value, targetProperty));
                        }
                    }
                }
            }

            var resultArray = newMappings.ToArray();
            CachedMappings[key] = resultArray;
            return resultArray;
        }
    }

    /// <summary>
    /// Maps properties from a source object to a target object.
    /// </summary>
    /// <typeparam name="TSource">The type of the source object.</typeparam>
    /// <typeparam name="TTarget">The type of the target object.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="configure">A configuration action to run after mapping.</param>
    /// <param name="skipNulls">Whether to skip null properties during mapping.</param>
    /// <param name="ignoreMissing">Whether to ignore properties missing in the target type.</param>
    /// <param name="performanceLogging">Whether to log performance metrics.</param>
    /// <returns>A new object of the target type with properties mapped from the source object.</returns>
    public static TTarget Map<TSource, TTarget>(TSource source, Action<TTarget> configure = null, bool skipNulls = false, bool ignoreMissing = false, bool performanceLogging = false) where TTarget : new()
    {
        DateTime startTime = DateTime.Now;

        if (source == null)
        {
            throw new ArgumentNullException(nameof(source), "Source object cannot be null");
        }

        Func<object, object> customMapper;
        lock (CustomMapperLock)
        {
            string key = $"{typeof(TSource).FullName}->{typeof(TTarget).FullName}";
            if (CustomMappers.TryGetValue(key, out customMapper))
            {
                return (TTarget)customMapper(source);
            }
        }

        if (typeof(IEnumerable).IsAssignableFrom(typeof(TTarget)) && source is IEnumerable sourceEnumerable)
        {
            return (TTarget)MapCollection(sourceEnumerable, typeof(TTarget));
        }

        TTarget target = new TTarget();
        var mappings = GetCachedMappings(typeof(TSource), typeof(TTarget));

        foreach (var mapping in mappings)
        {
            if (ignoreMissing && mapping.TargetProperty == null)
            {
                continue;
            }

            var sourceValue = mapping.SourceProperty.GetValue(source);

            var skipIfNullAttr = mapping.SourceProperty.GetCustomAttribute<SkipIfNullAttribute>();

            if (sourceValue == null && skipIfNullAttr != null)
            {
                continue;
            }

            if (skipNulls && sourceValue == null)
            {
                continue;
            }

            if (sourceValue == null)
            {
                var defaultValueAttr = mapping.TargetProperty.GetCustomAttribute<DefaultValueAttribute>();
                if (defaultValueAttr != null)
                {
                    sourceValue = defaultValueAttr.DefaultValue;
                }
            }

            if (mapping.SourceProperty.PropertyType != mapping.TargetProperty.PropertyType)
            {
                try
                {
                    var converter = TypeConverters.FirstOrDefault(c => c.CanConvert(mapping.SourceProperty.PropertyType, mapping.TargetProperty.PropertyType));

                    if (converter != null)
                    {
                        var convertedValue = converter.Convert(sourceValue, mapping.TargetProperty.PropertyType);
                        mapping.TargetProperty.SetValue(target, convertedValue);
                        continue;
                    }

                    var fallbackValue = Convert.ChangeType(sourceValue, mapping.TargetProperty.PropertyType);
                    mapping.TargetProperty.SetValue(target, fallbackValue);
                    continue;
                }
                catch (InvalidCastException)
                {
                    throw new InvalidCastException($"Cannot convert from {mapping.SourceProperty.PropertyType} to {mapping.TargetProperty.PropertyType}");
                }
            }

            var customConverterAttr = mapping.SourceProperty.GetCustomAttribute<CustomConverterAttribute>();
            if (customConverterAttr != null)
            {
                var converter = (ICustomPropertyConverter)Activator.CreateInstance(customConverterAttr.ConverterType);
                sourceValue = converter.Convert(sourceValue);
            }

            mapping.TargetProperty.SetValue(target, sourceValue);
        }

        configure?.Invoke(target);

        if (performanceLogging)
        {
            DateTime endTime = DateTime.Now;
            TimeSpan elapsedTime = endTime - startTime;
            Console.WriteLine($"Mapping took {elapsedTime.TotalMilliseconds} ms");
        }

        return target;
    }

    /// <summary>
    /// Maps properties from a source object to a new target object.
    /// </summary>
    /// <typeparam name="TSource">The type of the source object.</typeparam>
    /// <typeparam name="TTarget">The type of the target object.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="skipNulls">Whether to skip null properties during mapping.</param>
    /// <param name="ignoreMissing">Whether to ignore properties missing in the target type.</param>
    /// <param name="performanceLogging">Whether to log performance metrics.</param>
    /// <returns>A new object of the target type with properties mapped from the source object.</returns>
    public static TTarget Map<TSource, TTarget>(TSource source, bool skipNulls = false, bool ignoreMissing = false, bool performanceLogging = false) where TTarget : new()
    {
        // Capture the start time for performance logging
        DateTime startTime = DateTime.Now;

        // Generate a unique key for the source and target types
        string key = $"{typeof(TSource).FullName}->{typeof(TTarget).FullName}";

        // Check the cache for an existing delegate
        if (!CachedDelegates.TryGetValue(key, out var mapDelegate))
        {
            // Create a dynamic method for this specific mapping
            var dynamicMethod = new DynamicMethod($"Map_{key}", typeof(TTarget), new[] { typeof(TSource) }, true);
            var generator = dynamicMethod.GetILGenerator();

            // Declare local variables and get constructors
            var targetConstructor = typeof(TTarget).GetConstructor(Type.EmptyTypes);
            var local = generator.DeclareLocal(typeof(TTarget));

            // Instantiate the target object
            generator.Emit(OpCodes.Newobj, targetConstructor);
            generator.Emit(OpCodes.Stloc, local);

            // Loop through all properties of the source type
            foreach (var prop in typeof(TSource).GetProperties())
            {
                // Try to get the corresponding property in the target type
                var targetProp = typeof(TTarget).GetProperty(prop.Name);

                // Skip the property if it does not exist in the target type and ignoreMissing is true
                if (targetProp == null && ignoreMissing)
                {
                    continue;
                }

                // Generate mapping logic for the property
                if (targetProp != null)
                {
                    var notNullLabel = generator.DefineLabel();

                    // Skip null values if skipNulls is true
                    if (skipNulls && !prop.PropertyType.IsValueType)
                    {
                        generator.Emit(OpCodes.Ldarg_0);
                        generator.Emit(OpCodes.Callvirt, prop.GetGetMethod());
                        generator.Emit(OpCodes.Brtrue_S, notNullLabel);
                    }

                    // Map the property from the source object to the target object
                    generator.Emit(OpCodes.Ldloc, local);
                    generator.Emit(OpCodes.Ldarg_0);
                    generator.Emit(OpCodes.Callvirt, prop.GetGetMethod());
                    generator.Emit(OpCodes.Callvirt, targetProp.GetSetMethod());

                    // Mark the end of the null check
                    if (skipNulls && !prop.PropertyType.IsValueType)
                    {
                        generator.MarkLabel(notNullLabel);
                    }
                }
            }

            // Return the mapped object
            generator.Emit(OpCodes.Ldloc, local);
            generator.Emit(OpCodes.Ret);

            // Cache the delegate for future use
            mapDelegate = dynamicMethod.CreateDelegate(typeof(Func<TSource, TTarget>));
            CachedDelegates[key] = mapDelegate;
        }

        // Invoke the delegate to perform the mapping
        var result = ((Func<TSource, TTarget>)mapDelegate)(source);

        // Log the performance metrics if required
        if (performanceLogging)
        {
            DateTime endTime = DateTime.Now;
            TimeSpan elapsedTime = endTime - startTime;
            Console.WriteLine($"IL Mapping took {elapsedTime.TotalMilliseconds} ms");
        }

        return result;
    }


    private static Dictionary<Type, Delegate> _gettersCache = new Dictionary<Type, Delegate>();
    private static Dictionary<Type, Delegate> _settersCache = new Dictionary<Type, Delegate>();

    /// <summary>
    /// Retrieves or generates a delegate that gets the value of a specified property.
    /// </summary>
    /// <param name="property">The property for which to get the getter delegate.</param>
    /// <returns>A function delegate that takes an instance and returns the value of the property for that instance.</returns>
    private static Func<object, object> GetGetter(PropertyInfo property)
    {
        if (!_gettersCache.TryGetValue(property.PropertyType, out var getter))
        {
            var parameter = Expression.Parameter(typeof(object));
            var cast = Expression.Convert(parameter, property.DeclaringType);
            var propertyAccess = Expression.Property(cast, property);
            getter = Expression.Lambda<Func<object, object>>(Expression.Convert(propertyAccess, typeof(object)), parameter).Compile();
            _gettersCache[property.PropertyType] = getter;
        }

        return (Func<object, object>)getter;
    }

    /// <summary>
    /// Retrieves or generates a delegate that sets the value of a specified property.
    /// </summary>
    /// <param name="property">The property for which to get the setter delegate.</param>
    /// <returns>An action delegate that takes an instance and a value, and sets the property value on the instance.</returns>
    private static Action<object, object> GetSetter(PropertyInfo property)
    {
        if (!_settersCache.TryGetValue(property.PropertyType, out var setter))
        {
            var instance = Expression.Parameter(typeof(object));
            var argument = Expression.Parameter(typeof(object));
            var instanceCast = Expression.Convert(instance, property.DeclaringType);
            var argumentCast = Expression.Convert(argument, property.PropertyType);
            var propertyAccess = Expression.Property(instanceCast, property);
            setter = Expression.Lambda<Action<object, object>>(Expression.Assign(propertyAccess, argumentCast), instance, argument).Compile();
            _settersCache[property.PropertyType] = setter;
        }

        return (Action<object, object>)setter;
    }

    /// <summary>
    /// Maps an IEnumerable source collection to a target collection of a specified type.
    /// </summary>
    /// <param name="source">The source collection to map.</param>
    /// <param name="targetType">The type of the target collection.</param>
    /// <returns>A new collection of the target type containing the mapped items.</returns>
    private static object MapCollection(IEnumerable source, Type targetType)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source), "Source collection cannot be null");
        }

        var itemType = targetType.IsArray ? targetType.GetElementType() : targetType.GetGenericArguments()[0];
        var targetListType = typeof(List<>).MakeGenericType(itemType);
        var targetList = (IList)Activator.CreateInstance(targetListType);

        foreach (var sourceItem in source)
        {
            var targetItem = Activator.CreateInstance(itemType);
            var mappings = GetCachedMappings(sourceItem.GetType(), itemType);

            foreach (var mapping in mappings)
            {
                var getter = GetGetter(mapping.SourceProperty);
                var setter = GetSetter(mapping.TargetProperty);

                var sourceValue = getter(sourceItem);

                if (mapping.SourceProperty.PropertyType != mapping.TargetProperty.PropertyType)
                {
                    try
                    {
                        var convertedValue = Convert.ChangeType(sourceValue, mapping.TargetProperty.PropertyType);
                        mapping.TargetProperty.SetValue(targetItem, convertedValue);
                        continue;
                    }
                    catch (InvalidCastException)
                    {
                        throw new InvalidCastException($"Cannot convert from {mapping.SourceProperty.PropertyType} to {mapping.TargetProperty.PropertyType}");
                    }
                }

                setter(targetItem, sourceValue);
            }

            targetList.Add(targetItem);
        }

        return targetType.IsArray ? ConvertListToArray(targetList, itemType) : targetList;
    }

    /// <summary>
    /// Converts an IList to an array of a specified item type.
    /// </summary>
    /// <param name="list">The list to convert.</param>
    /// <param name="itemType">The type of items in the resulting array.</param>
    /// <returns>An array containing the elements from the list, cast to the specified item type.</returns>
    private static Array ConvertListToArray(IList list, Type itemType)
    {
        int count = list.Count;
        var array = Array.CreateInstance(itemType, count);

        object[] objectArray = array as object[];

        if (objectArray != null)
        {
            for (int i = 0; i < count; ++i)
            {
                objectArray[i] = list[i];
            }
        }
        else
        {
            for (int i = 0; i < count; ++i)
            {
                array.SetValue(list[i], i);
            }
        }

        return array;
    }

}
