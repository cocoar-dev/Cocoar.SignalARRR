package dev.cocoar.signalarrr.integration.contracts

import dev.cocoar.signalarrr.GenericArguments
import dev.cocoar.signalarrr.HubMethod
import dev.cocoar.signalarrr.HubProxy
import dev.cocoar.signalarrr.ProxyKind
import kotlinx.coroutines.flow.Flow

/** Mirrors `Cocoar.SignalARRR.Tests.SharedModels.ITestServerMethods` on the IntegrationTestServer. */
@HubProxy(name = "Cocoar.SignalARRR.Tests.SharedModels.ITestServerMethods")
interface ITestServerMethods {
    suspend fun getName(): String
    suspend fun getNameAsync(): String
    suspend fun getGuid(): String
    suspend fun getGuidAsync(): String
    suspend fun nothing()
    suspend fun nothingAsync()
}

/** Hub-level methods of `TestHub` (not an interface on the server; addressed by bare method name). */
@HubProxy(kind = ProxyKind.HUB)
interface TestHubMethods {
    suspend fun echo(message: String): String
    suspend fun getConnectionId(): String
    fun counter(count: Int, delay: Int): Flow<Int>

    @HubMethod("Echo")
    suspend fun echoRenamed(message: String): String
}

/** Mirrors the `ExtraMethods : ServerMethods<TestHub>` class on the server. */
@HubProxy(name = "ExtraMethods", kind = ProxyKind.SERVER_METHODS)
interface ExtraMethods {
    suspend fun greet(name: String): String
    suspend fun add(a: Int, b: Int): Int
    @HubMethod("CustomEcho")
    suspend fun echoWithCustomName(input: String): String
    suspend fun generateItems(count: Int): List<String>
    suspend fun wordLengths(sentence: String): Map<String, Int>
    suspend fun combine(text: String, number: Int, flag: Boolean): String
    suspend fun readStreamContent(data: ByteArray): String
}
