# QuickMapper: A Simple Object-to-Object Mapping Library for C#

## Overview

QuickMapper is a lightweight and straightforward library designed to simplify object-to-object mapping in .NET applications. It offers various features to optimize performance and simplify the mapping process, such as caching, custom mapping, and type conversion. The library also supports collections and allows users to skip null values or ignore missing properties during mapping.

## Features

- **Custom Mapping**: Ability to register custom mapping functions for specific type pairs.
- **Caching**: Mappings and delegates are cached for optimized performance.
- **Type Conversion**: Easily extendable to include custom type converters.
- **IL Code Generation**: Generate IL code for mappings dynamically.
- **Skip Nulls**: Optional skipping of null values during mapping.
- **Ignore Missing Properties**: Optional ignoring of missing properties during mapping.
- **Performance Logging**: Log the performance metrics for the mapping operations.
- **Easy-to-Use Extension Methods**: Additional extension methods provided for even more straightforward usage.

## Installation

Download the library from NuGet or compile it from source code.

## Usage

### Basic Mapping

```csharp
var source = new SourceType { Property1 = "value1", Property2 = 42 };
var target = QuickMapper.Map<SourceType, TargetType>(source);
```

### Using Extension Methods

```csharp
var target = source.MapTo<TargetType>();
```

### Custom Configuration

```csharp
var target = source.MapTo<TargetType>(target => { target.SomeProperty = "custom value"; });
```

### Register Custom Mapper

```csharp
QuickMapper.RegisterCustomMapper<SourceType, TargetType>(source => new TargetType { Property1 = source.Property1 });
```

### Register Type Converter

```csharp
QuickMapper.RegisterTypeConverter(new CustomTypeConverter());
```

### Skipping Nulls and Ignoring Missing Properties

```csharp
var target = source.MapTo<TargetType>(true, true);
```

## Extension Methods

We also provide a set of convenient extension methods for easier object-to-object mapping. Simply include the `QuickMapper.Extensions` namespace to use these methods.
