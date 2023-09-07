using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace QuickMapper;

/// <summary>
/// Provides a mechanism for mapping properties between objects of different types.
/// </summary>
public static class QuickMapper
{
    private static readonly Lazy<Dictionary<string, Func<object, object>>> LazyCustomMappers =
    new Lazy<Dictionary<string, Func<object, object>>>(() => new Dictionary<string, Func<object, object>>());
    private static Dictionary<string, Func<object, object>> CustomMappers => LazyCustomMappers.Value;

    private static readonly Lazy<Dictionary<string, (PropertyInfo SourceProperty, PropertyInfo TargetProperty)[]>> LazyCachedMappings =
      new Lazy<Dictionary<string, (PropertyInfo SourceProperty, PropertyInfo TargetProperty)[]>>(() => new Dictionary<string, (PropertyInfo SourceProperty, PropertyInfo TargetProperty)[]>());
    private static Dictionary<string, (PropertyInfo SourceProperty, PropertyInfo TargetProperty)[]> CachedMappings => LazyCachedMappings.Value;

    private static readonly object CustomMapperLock = new object();
    private static readonly object CachedMappingLock = new object();
    private static readonly Lazy<List<ITypeConverter>> LazyTypeConverters =
     new Lazy<List<ITypeConverter>>(() => new List<ITypeConverter>());
    private static List<ITypeConverter> TypeConverters => LazyTypeConverters.Value;


    private static readonly ConcurrentDictionary<string, Delegate> CachedDelegates = new ConcurrentDictionary<string, Delegate>();
    static ConcurrentDictionary<string, List<PropertyMatchingInfo>> CachedMatchingProps = new ConcurrentDictionary<string, List<PropertyMatchingInfo>>();

    public static void RegisterTypeConverter(ITypeConverter converter)
    {
        TypeConverters.Add(converter);
    }

    public static void RegisterCustomMapper<TSource, TTarget>(Func<TSource, TTarget> mapper)
    {
        lock (CustomMapperLock)
        {
            string key = $"{typeof(TSource).FullName}->{typeof(TTarget).FullName}";
            CustomMappers[key] = x => mapper((TSource)x);
        }
    }

    private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> SourceTypePropertiesCache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
    private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> TargetTypePropertiesCache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();

    private static (PropertyInfo SourceProperty, PropertyInfo TargetProperty)[] GetCachedMappings(Type sourceType, Type targetType)
    {
        string key = $"{sourceType.FullName}->{targetType.FullName}";

        lock (CachedMappingLock)
        {
            if (CachedMappings.TryGetValue(key, out var mappings))
            {
                return mappings;
            }
        }

        if (!SourceTypePropertiesCache.TryGetValue(sourceType, out var sourceProperties))
        {
            sourceProperties = sourceType.GetProperties().ToDictionary(p => p.Name, p => p);
            SourceTypePropertiesCache[sourceType] = sourceProperties;
        }

        if (!TargetTypePropertiesCache.TryGetValue(targetType, out var targetProperties))
        {
            targetProperties = targetType.GetProperties().ToDictionary(p => p.Name, p => p);
            TargetTypePropertiesCache[targetType] = targetProperties;
        }

        List<(PropertyInfo SourceProperty, PropertyInfo TargetProperty)> newMappings = new List<(PropertyInfo SourceProperty, PropertyInfo TargetProperty)>();

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

        lock (CachedMappingLock)
        {
            CachedMappings[key] = resultArray;
        }

        return resultArray;
    }

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


    public static TTarget Map<TSource, TTarget>(TSource source, bool skipNulls = false) where TTarget : new()
    {
        return PerformMapping<TSource, TTarget>(source, skipNulls);
    }

    public static IEnumerable<TTarget> MapBatch<TSource, TTarget>(IEnumerable<TSource> sources, bool skipNulls = false) where TTarget : new()
    {
        Func<TSource, TTarget> mapperFunc = PerformMapping<TSource, TTarget>(skipNulls);
        return sources.Select(source => mapperFunc(source)).ToList();
    }

    private static TTarget PerformMapping<TSource, TTarget>(TSource source, bool skipNulls = false) where TTarget : new()
    {
        var mapperFunc = PerformMapping<TSource, TTarget>(skipNulls);
        return mapperFunc(source);
    }

    private static Func<TSource, TTarget> PerformMapping<TSource, TTarget>(bool skipNulls = false) where TTarget : new()
    {
        string key = $"{typeof(TSource).FullName}->{typeof(TTarget).FullName}";

        if (!CachedDelegates.TryGetValue(key, out var mapDelegate))
        {
            mapDelegate = GenerateMappingDelegate<TSource, TTarget>(skipNulls);
            CachedDelegates[key] = mapDelegate;
        }

        return (Func<TSource, TTarget>)mapDelegate;
    }


    private static Delegate GenerateMappingDelegate<TSource, TTarget>(bool skipNulls)
    {
        var dynamicMethod = CreateDynamicMappingMethod<TSource, TTarget>();
        var generator = dynamicMethod.GetILGenerator();
        var targetConstructor = typeof(TTarget).GetConstructor(Type.EmptyTypes);
        var local = generator.DeclareLocal(typeof(TTarget));

        generator.Emit(OpCodes.Newobj, targetConstructor);
        generator.Emit(OpCodes.Stloc, local);

        var key = $"{typeof(TSource).FullName}->{typeof(TTarget).FullName}";
        var matchingProps = CachedMatchingProps.GetOrAdd(key, k => FindMatchingProperties<TSource, TTarget>());

        EmitPropertyMappingILCode(generator, local, matchingProps, skipNulls);

        return dynamicMethod.CreateDelegate(typeof(Func<TSource, TTarget>));
    }

    private static DynamicMethod CreateDynamicMappingMethod<TSource, TTarget>()
    {
        var key = $"{typeof(TSource).FullName}->{typeof(TTarget).FullName}";
        return new DynamicMethod($"Map_{key}", typeof(TTarget), new[] { typeof(TSource) }, true);
    }


    private static void EmitPropertyMappingILCode(ILGenerator generator, LocalBuilder local, List<PropertyMatchingInfo> matchingProps, bool skipNulls)
    {
        var getSourceMethods = new List<MethodInfo>();
        var setTargetMethods = new List<MethodInfo>();
        var notNullLabels = new List<Label>();

        foreach (var match in matchingProps)
        {
            getSourceMethods.Add(match.SourceProperty.GetGetMethod());
            setTargetMethods.Add(match.TargetProperty.GetSetMethod());

            if (skipNulls && !match.SourceProperty.PropertyType.IsValueType)
            {
                notNullLabels.Add(generator.DefineLabel());
            }
            else
            {
                notNullLabels.Add(default);
            }
        }

        for (int i = 0; i < matchingProps.Count; i++)
        {
            if (skipNulls && notNullLabels[i] != default)
            {
                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Callvirt, getSourceMethods[i]);
                generator.Emit(OpCodes.Brtrue_S, notNullLabels[i]);
            }

            generator.Emit(OpCodes.Ldloc, local);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Callvirt, getSourceMethods[i]);
            generator.Emit(OpCodes.Callvirt, setTargetMethods[i]);

            if (skipNulls && notNullLabels[i] != default)
            {
                generator.MarkLabel(notNullLabels[i]);
            }
        }

        generator.Emit(OpCodes.Ldloc, local);
        generator.Emit(OpCodes.Ret);
    }


    private static readonly Dictionary<string, List<PropertyMatchingInfo>> CachedMatchingProperties = new Dictionary<string, List<PropertyMatchingInfo>>();

    private static List<PropertyMatchingInfo> FindMatchingProperties<TSource, TTarget>()
    {
        var key = $"{typeof(TSource).FullName}->{typeof(TTarget).FullName}";
        if (CachedMatchingProperties.TryGetValue(key, out var cachedResult))
        {
            return cachedResult;
        }

        var sourceProps = typeof(TSource).GetProperties();
        var targetPropsDict = typeof(TTarget).GetProperties().ToDictionary(p => p.Name, p => p);

        var matchingProps = new List<PropertyMatchingInfo>();
        foreach (var sourceProp in sourceProps)
        {
            if (targetPropsDict.TryGetValue(sourceProp.Name, out var targetProp))
            {
                matchingProps.Add(new PropertyMatchingInfo
                {
                    SourceProperty = sourceProp,
                    TargetProperty = targetProp
                });
            }
        }

        CachedMatchingProperties[key] = matchingProps;
        return matchingProps;
    }


    private static readonly Lazy<ConcurrentDictionary<PropertyInfo, Func<object, object>>> LazyGettersCache =
    new Lazy<ConcurrentDictionary<PropertyInfo, Func<object, object>>>(() => new ConcurrentDictionary<PropertyInfo, Func<object, object>>());
    private static ConcurrentDictionary<PropertyInfo, Func<object, object>> _gettersCache => LazyGettersCache.Value;

    private static readonly Lazy<ConcurrentDictionary<PropertyInfo, Action<object, object>>> LazySettersCache =
        new Lazy<ConcurrentDictionary<PropertyInfo, Action<object, object>>>(() => new ConcurrentDictionary<PropertyInfo, Action<object, object>>());
    private static ConcurrentDictionary<PropertyInfo, Action<object, object>> _settersCache => LazySettersCache.Value;


    private static Func<object, object> GetGetter(PropertyInfo property)
    {
        return _gettersCache.GetOrAdd(property, prop => {
            var parameter = Expression.Parameter(typeof(object));
            var cast = Expression.Convert(parameter, prop.DeclaringType);
            var propertyAccess = Expression.Property(cast, prop);
            var getterExpr = Expression.Lambda<Func<object, object>>(Expression.Convert(propertyAccess, typeof(object)), parameter);
            return getterExpr.Compile();
        });
    }

    private static Action<object, object> GetSetter(PropertyInfo property)
    {
        return _settersCache.GetOrAdd(property, prop => {
            var instance = Expression.Parameter(typeof(object));
            var argument = Expression.Parameter(typeof(object));
            var instanceCast = Expression.Convert(instance, prop.DeclaringType);
            var argumentCast = Expression.Convert(argument, prop.PropertyType);
            var propertyAccess = Expression.Property(instanceCast, prop);
            var setterExpr = Expression.Lambda<Action<object, object>>(Expression.Assign(propertyAccess, argumentCast), instance, argument);
            return setterExpr.Compile();
        });
    }

    private static object MapCollection(IEnumerable source, Type targetType)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source), "Source collection cannot be null");
        }

        var itemType = targetType.IsArray ? targetType.GetElementType() : targetType.GetGenericArguments()[0];
        var targetListType = typeof(List<>).MakeGenericType(itemType);
        var targetList = (IList)Activator.CreateInstance(targetListType);

        // Create a single instance to get the mappings and cache the getters and setters
        var firstSourceItem = source.Cast<object>().FirstOrDefault();
        if (firstSourceItem == null) return targetList; // Empty source

        var mappings = GetCachedMappings(firstSourceItem.GetType(), itemType);
        var getters = mappings.Select(m => GetGetter(m.SourceProperty)).ToArray();
        var setters = mappings.Select(m => GetSetter(m.TargetProperty)).ToArray();

        foreach (var sourceItem in source)
        {
            var targetItem = DynamicInitializer.CreateAndInitialize(itemType);

            for (int i = 0; i < mappings.Length; i++)
            {
                var sourceValue = getters[i](sourceItem);
                if (mappings[i].SourceProperty.PropertyType != mappings[i].TargetProperty.PropertyType)
                {
                    try
                    {
                        var convertedValue = Convert.ChangeType(sourceValue, mappings[i].TargetProperty.PropertyType);
                        setters[i](targetItem, convertedValue);
                        continue;
                    }
                    catch (InvalidCastException)
                    {
                        throw new InvalidCastException($"Cannot convert from {mappings[i].SourceProperty.PropertyType} to {mappings[i].TargetProperty.PropertyType}");
                    }
                }

                setters[i](targetItem, sourceValue);
            }
            targetList.Add(targetItem);
        }

        return targetType.IsArray ? ConvertListToArray(targetList, itemType) : targetList;
    }

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
