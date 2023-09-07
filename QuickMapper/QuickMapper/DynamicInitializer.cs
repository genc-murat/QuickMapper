using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace QuickMapper;

public class DynamicInitializer
{
    private static readonly ConcurrentDictionary<Type, Func<object>> _cache = new ConcurrentDictionary<Type, Func<object>>();

    public static object CreateAndInitialize(Type type)
    {
        // Retrieve the cached delegate if available
        if (!_cache.TryGetValue(type, out Func<object> initializer))
        {
            // Create a delegate for object creation and initialization
            var newExpr = Expression.New(type);
            var lambda = Expression.Lambda<Func<object>>(Expression.Convert(newExpr, typeof(object)));
            initializer = lambda.Compile();

            // Add the new delegate to the cache
            _cache[type] = initializer;
        }

        // Use the delegate to create and initialize an object
        return initializer();
    }
}
