// -----------------------------------------------------------------------
// <copyright file="Bind.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Bind methods.
/// </summary>
internal static class Bind
{
    /// <summary>
    /// Gets the binder from the service provider.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    /// <returns>The binder.</returns>
    public static System.CommandLine.Binding.BinderBase<T> FromServiceProvider<T>() => ServiceProviderBinder<T>.Instance;

    private sealed class ServiceProviderBinder<T> : System.CommandLine.Binding.BinderBase<T>
    {
        public static ServiceProviderBinder<T> Instance { get; } = new();

        protected override T GetBoundValue(System.CommandLine.Binding.BindingContext bindingContext) => (T)bindingContext.GetRequiredService(typeof(T));
    }
}