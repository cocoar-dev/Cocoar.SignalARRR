using System;
using Cocoar.SignalARRR.SourceGenerator.Helpers;

namespace Cocoar.SignalARRR.SourceGenerator.Model;

internal readonly struct ContractParameterInfo : IEquatable<ContractParameterInfo> {
    public string TypeFullName { get; }
    public string Name { get; }
    public bool HasDefaultValue { get; }
    public string? DefaultValueLiteral { get; }
    public bool IsCancellationToken { get; }
    /// <summary>The type without a nullable-reference annotation, usable in <c>typeof</c>.</summary>
    public string ClrTypeName { get; }
    public bool IsStream { get; }

    public ContractParameterInfo(
        string typeFullName,
        string name,
        bool hasDefaultValue,
        string? defaultValueLiteral,
        bool isCancellationToken,
        string clrTypeName,
        bool isStream) {
        TypeFullName = typeFullName;
        Name = name;
        HasDefaultValue = hasDefaultValue;
        DefaultValueLiteral = defaultValueLiteral;
        IsCancellationToken = isCancellationToken;
        ClrTypeName = clrTypeName;
        IsStream = isStream;
    }

    public bool Equals(ContractParameterInfo other) =>
        TypeFullName == other.TypeFullName &&
        Name == other.Name &&
        HasDefaultValue == other.HasDefaultValue &&
        DefaultValueLiteral == other.DefaultValueLiteral &&
        IsCancellationToken == other.IsCancellationToken &&
        ClrTypeName == other.ClrTypeName &&
        IsStream == other.IsStream;

    public override bool Equals(object? obj) => obj is ContractParameterInfo other && Equals(other);

    public override int GetHashCode() {
        var hash = HashCombine.Of(TypeFullName);
        hash = HashCombine.Combine(hash, HashCombine.Of(Name));
        hash = HashCombine.Combine(hash, HasDefaultValue ? 1 : 0);
        hash = HashCombine.Combine(hash, HashCombine.Of(DefaultValueLiteral));
        hash = HashCombine.Combine(hash, IsCancellationToken ? 1 : 0);
        hash = HashCombine.Combine(hash, HashCombine.Of(ClrTypeName));
        hash = HashCombine.Combine(hash, IsStream ? 1 : 0);
        return hash;
    }
}