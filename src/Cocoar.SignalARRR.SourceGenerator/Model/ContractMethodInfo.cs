using System;
using Cocoar.SignalARRR.SourceGenerator.Helpers;

namespace Cocoar.SignalARRR.SourceGenerator.Model;

internal readonly struct ContractMethodInfo : IEquatable<ContractMethodInfo> {
    public string Name { get; }
    /// <summary>The name the member is addressed by on the wire — [MessageName] if present, else <see cref="Name"/>.</summary>
    public string WireName { get; }
    public ReturnTypeCategory ReturnCategory { get; }
    public string ReturnTypeFullName { get; }
    public string? ElementTypeFullName { get; }
    public EquatableArray<ContractParameterInfo> Parameters { get; }
    public EquatableArray<string> TypeParameterNames { get; }

    public ContractMethodInfo(
        string name,
        string wireName,
        ReturnTypeCategory returnCategory,
        string returnTypeFullName,
        string? elementTypeFullName,
        EquatableArray<ContractParameterInfo> parameters,
        EquatableArray<string> typeParameterNames) {
        Name = name;
        WireName = wireName;
        ReturnCategory = returnCategory;
        ReturnTypeFullName = returnTypeFullName;
        ElementTypeFullName = elementTypeFullName;
        Parameters = parameters;
        TypeParameterNames = typeParameterNames;
    }

    public bool Equals(ContractMethodInfo other) =>
        Name == other.Name &&
        WireName == other.WireName &&
        ReturnCategory == other.ReturnCategory &&
        ReturnTypeFullName == other.ReturnTypeFullName &&
        ElementTypeFullName == other.ElementTypeFullName &&
        Parameters.Equals(other.Parameters) &&
        TypeParameterNames.Equals(other.TypeParameterNames);

    public override bool Equals(object? obj) => obj is ContractMethodInfo other && Equals(other);

    public override int GetHashCode() {
        var hash = HashCombine.Of(Name);
        hash = HashCombine.Combine(hash, HashCombine.Of(WireName));
        hash = HashCombine.Combine(hash, (int)ReturnCategory);
        hash = HashCombine.Combine(hash, HashCombine.Of(ReturnTypeFullName));
        hash = HashCombine.Combine(hash, HashCombine.Of(ElementTypeFullName));
        hash = HashCombine.Combine(hash, Parameters.GetHashCode());
        hash = HashCombine.Combine(hash, TypeParameterNames.GetHashCode());
        return hash;
    }
}
