﻿// Copyright (c) Microsoft. All rights reserved.

using System.Linq;

#pragma warning disable IDE0130
namespace Microsoft.SemanticKernel.Planning;
#pragma warning restore IDE0130

/// <summary>
/// Provides extension methods for the <see cref="SKFunctionMetadata"/> class.
/// </summary>
internal static class SKFunctionMetadataExtensions
{
    /// <summary>
    /// Create a manual-friendly string for a function.
    /// </summary>
    /// <param name="function">The function to create a manual-friendly string for.</param>
    /// <returns>A manual-friendly string for a function.</returns>
    internal static string ToManualString(this SKFunctionMetadata function)
    {
        var inputs = string.Join("\n", function.Parameters.Select(parameter =>
        {
            var defaultValueString = string.IsNullOrEmpty(parameter.DefaultValue) ? string.Empty : $" (default value: {parameter.DefaultValue})";
            return $"    - {parameter.Name}: {parameter.Description}{defaultValueString}";
        }));

        // description and inputs are indented by 2 spaces
        // While each parameter in inputs is indented by 4 spaces
        return $@"{function.ToFullyQualifiedName()}:
  description: {function.Description}
  inputs:
{inputs}";
    }

    /// <summary>
    /// Create a fully qualified name for a function.
    /// </summary>
    /// <param name="function">The function to create a fully qualified name for.</param>
    /// <returns>A fully qualified name for a function.</returns>
    internal static string ToFullyQualifiedName(this SKFunctionMetadata function)
    {
        return $"{function.PluginName}.{function.Name}";
    }

    /// <summary>
    /// Create a string for generating an embedding for a function.
    /// </summary>
    /// <param name="function">The function to create a string for generating an embedding for.</param>
    /// <returns>A string for generating an embedding for a function.</returns>
    internal static string ToEmbeddingString(this SKFunctionMetadata function)
    {
        var inputs = string.Join("\n", function.Parameters.Select(p => $"    - {p.Name}: {p.Description}"));
        return $"{function.Name}:\n  description: {function.Description}\n  inputs:\n{inputs}";
    }
}
