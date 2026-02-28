using System;
using Cocoar.SignalARRR.SourceGenerator.Helpers;

namespace Cocoar.SignalARRR.SourceGenerator.Model;

internal readonly struct ContractParameterInfo : IEquatable<ContractParameterInfo> {
    public string TypeFullName { get; }
    public string Name { get; }
    public bool HasDefaultValue { get; }
    public string? DefaultValueLiteral { get; }
    public bool IsCancellationToken { get; }

    public ContractParameterInfo(
        string typeFullName,
        string name,
        bool hasDefaultValue,
        string? defaultValueLiteral,
        bool isCancellationToken) {
        TypeFullName = typeFullName;
        Name = name;
        HasDefaultValue = hasDefaultValue;
        DefaultValueLiteral = defaultValueLiteral;
        IsCancellationToken = isCancellationToken;
    }

    public bool Equals(ContractParameterInfo other) =>
        TypeFullName == other.TypeFullName &&
        Name == other.Name &&
        HasDefaultValue == other.HasDefaultValue &&
        DefaultValueLiteral == other.DefaultValueLiteral &&
        IsCancellationToken == other.IsCancellationToken;

    public override bool Equals(object? obj) => obj is ContractParameterInfo other && Equals(other);

    public override int GetHashCode() {
        var hash = HashCombine.Of(TypeFullName);
        hash = HashCombine.Combine(hash, HashCombine.Of(Name));
        hash = HashCombine.Combine(hash, HasDefaultValue ? 1 : 0);
        hash = HashCombine.Combine(hash, HashCombine.Of(DefaultValueLiteral));
        hash = HashCombine.Combine(hash, IsCancellationToken ? 1 : 0);
        return hash;
    }
}
