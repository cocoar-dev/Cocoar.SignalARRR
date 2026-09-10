package dev.cocoar.signalarrr

/** How a generated proxy addresses its target on the server. */
public enum class ProxyKind {
    /** A contract interface: `Interface|Method`. */
    INTERFACE,

    /** A `ServerMethods<THub>` class: `ClassName.Method`. */
    SERVER_METHODS,

    /** Methods declared on the hub class itself: bare `Method`; [HubProxy.name] is ignored. */
    HUB,
}

/**
 * Marks an interface for typed proxy generation. The KSP processor (`dev.cocoar:signalarrr-ksp`)
 * generates `<Name>Proxy`, routing each member to the connection:
 *
 * | Member | Generated call | Wire method |
 * |---|---|---|
 * | `suspend fun x(...)` without result | `connection.send` | `SendMessage` |
 * | `suspend fun x(...): T` | `connection.invoke<T>` | `InvokeMessageResult` |
 * | `fun x(...): Flow<T>` | `connection.stream<T>` | `StreamMessage` |
 *
 * The wire name of a member depends on [kind]: `<name>|<method>` for a contract interface,
 * `<name>.<method>` for a `ServerMethods<THub>` class, bare `<method>` for the hub class. [name]
 * defaults to the simple Kotlin interface name. Method names are capitalised to match .NET
 * conventions unless [pascalCase] is `false` or the member carries [HubMethod].
 *
 * @param name The class half of the wire name, e.g. `"MyApp.Contracts.IChatHub"` — the .NET
 *   full name of the contract (or the `[MessageName]` it declares), or the `ServerMethods` class name.
 * @param pascalCase Capitalise the first letter of each method name (`getHistory` → `GetHistory`).
 * @param kind What the interface stands for on the server.
 */
@Target(AnnotationTarget.CLASS)
@Retention(AnnotationRetention.SOURCE)
public annotation class HubProxy(
    val name: String = "",
    val pascalCase: Boolean = true,
    val kind: ProxyKind = ProxyKind.INTERFACE,
)

/** Overrides the method half of the wire name of one member of a [HubProxy] interface. */
@Target(AnnotationTarget.FUNCTION)
@Retention(AnnotationRetention.SOURCE)
public annotation class HubMethod(val name: String)

/**
 * Passes the .NET type names of a generic method's type arguments (`GenericArguments` on the
 * wire), for contracts with generic members such as `T Invoke<T>(string command)`.
 */
@Target(AnnotationTarget.FUNCTION)
@Retention(AnnotationRetention.SOURCE)
public annotation class GenericArguments(vararg val typeNames: String)

/** Creates the typed proxy of a contract. Generated proxies expose one as their companion object. */
public interface HubProxyFactory<T : Any> {
    public fun create(connection: HARRRConnection): T
}

/**
 * A handler object for a server-to-client contract: [interfaceName] is the interface half of the
 * wire name, [handlers] maps method names to handlers. Registered with [HARRRConnection.registerInterface].
 */
public interface ServerInterfaceHandler {
    public val interfaceName: String
    public fun handlers(): Map<String, ServerMethodHandler>
}
