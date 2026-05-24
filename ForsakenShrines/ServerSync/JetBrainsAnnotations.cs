// Minimal stubs for JetBrains.Annotations attributes used by ConfigSync.cs.
// Eliminates the JetBrains.Annotations NuGet dependency; these attributes are
// marker-only and have no runtime behaviour.
using System;

// ReSharper disable once CheckNamespace
namespace JetBrains.Annotations
{
    [AttributeUsage(AttributeTargets.All)]
    internal sealed class PublicAPIAttribute : Attribute
    {
        public PublicAPIAttribute() { }
        public PublicAPIAttribute(string comment) { }
    }

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class UsedImplicitlyAttribute : Attribute
    {
        public UsedImplicitlyAttribute() { }
    }
}
