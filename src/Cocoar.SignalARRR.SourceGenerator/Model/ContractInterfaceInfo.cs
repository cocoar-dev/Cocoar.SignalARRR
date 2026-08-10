using System;
using Cocoar.SignalARRR.SourceGenerator.Helpers;

namespace Cocoar.SignalARRR.SourceGenerator.Model;

internal readonly struct ContractInterfaceInfo : IEquatable<ContractInterfaceInfo> {
    public string Namespace { get; }
    public string InterfaceName { get; }
    public string FullName { get; }
    /// <summary>The name the interface is addressed by on the wire — [MessageName] if present, else <see cref="FullName"/>.</summary>
    public string WireName { get; }
    public string ProxyClassName { get; }
    public EquatableArray<ContractMethodInfo> Methods { get; }

    public ContractInterfaceInfo(
        string @namespace,
        string interfaceName,
        string fullName,
        string wireName,
        string proxyClassName,
        EquatableArray<ContractMethodInfo> methods) {
        Namespace = @namespace;
        InterfaceName = interfaceName;
        FullName = fullName;
        WireName = wireName;
        ProxyClassName = proxyClassName;
        Methods = methods;
    }

    public bool Equals(ContractInterfaceInfo other) =>
        Namespace == other.Namespace &&
        InterfaceName == other.InterfaceName &&
        FullName == other.FullName &&
        WireName == other.WireName &&
        ProxyClassName == other.ProxyClassName &&
        Methods.Equals(other.Methods);

    public override bool Equals(object? obj) => obj is ContractInterfaceInfo other && Equals(other);

    public override int GetHashCode() {
        var hash = HashCombine.Of(Namespace);
        hash = HashCombine.Combine(hash, HashCombine.Of(InterfaceName));
        hash = HashCombine.Combine(hash, HashCombine.Of(FullName));
        hash = HashCombine.Combine(hash, HashCombine.Of(WireName));
        hash = HashCombine.Combine(hash, HashCombine.Of(ProxyClassName));
        hash = HashCombine.Combine(hash, Methods.GetHashCode());
        return hash;
    }
}
