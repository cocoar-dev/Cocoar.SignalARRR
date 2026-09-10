package dev.cocoar.signalarrr.integration

import dev.cocoar.signalarrr.ConsoleLogger
import dev.cocoar.signalarrr.HARRRConnection
import dev.cocoar.signalarrr.HARRRConnectionOptions
import dev.cocoar.signalarrr.LogLevel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assumptions.assumeTrue
import org.junit.jupiter.api.BeforeEach

/**
 * Connects to the shared IntegrationTestServer. The base URL comes from
 * `SIGNALARRR_TEST_SERVER_URL` (set by `scripts/run-integration-tests.sh`); without it every
 * test is skipped, not failed.
 */
abstract class IntegrationTestBase {

    companion object {
        val serverUrl: String? = System.getenv("SIGNALARRR_TEST_SERVER_URL")?.trim()?.takeIf { it.isNotEmpty() }?.trimEnd('/')
        const val HUB_PATH = "/signalr/testhub"
    }

    lateinit var connection: HARRRConnection

    /** Override to configure the connection (protocol, transports, ...). */
    open fun configure(options: HARRRConnectionOptions) {}

    @BeforeEach
    fun setUpConnection() {
        assumeTrue(serverUrl != null, "SIGNALARRR_TEST_SERVER_URL not set — skipping integration tests")
        connection = HARRRConnection.create("$serverUrl$HUB_PATH") {
            logger = ConsoleLogger(LogLevel.WARNING)
            configure(this)
        }
        runBlocking { withTimeout(20_000) { connection.start() } }
    }

    @AfterEach
    fun tearDownConnection() {
        if (::connection.isInitialized) runBlocking { connection.stop() }
    }

    /** Runs [block] with a deadline so a hanging call fails instead of stalling the suite. */
    fun test(timeoutMs: Long = 30_000, block: suspend () -> Unit) = runBlocking { withTimeout(timeoutMs) { block() } }

    suspend fun connectionId(): String = connection.invoke("GetConnectionId")

    suspend fun trigger(path: String, vararg params: Pair<String, String>): Pair<Int, String> = TestServer.trigger(path, *params)

    suspend fun get(path: String, vararg params: Pair<String, String>): Pair<Int, String> = TestServer.get(path, *params)
}

/** The test-only HTTP endpoints of the IntegrationTestServer (the ones under the `__test` prefix). */
object TestServer {
    private val http = OkHttpClient()

    /** POSTs to a trigger endpoint; returns (status, body). */
    suspend fun trigger(path: String, vararg params: Pair<String, String>): Pair<Int, String> = withContext(Dispatchers.IO) {
        val url = "${IntegrationTestBase.serverUrl}$path".toHttpUrl().newBuilder().apply {
            params.forEach { (k, v) -> addQueryParameter(k, v) }
        }.build()
        val request = Request.Builder().url(url).post(ByteArray(0).toRequestBody(null)).build()
        http.newCall(request).execute().use { it.code to it.body.string() }
    }

    /** GETs an endpoint; returns (status, body). */
    suspend fun get(path: String, vararg params: Pair<String, String>): Pair<Int, String> = withContext(Dispatchers.IO) {
        val url = "${IntegrationTestBase.serverUrl}$path".toHttpUrl().newBuilder().apply {
            params.forEach { (k, v) -> addQueryParameter(k, v) }
        }.build()
        http.newCall(Request.Builder().url(url).get().build()).execute().use { it.code to it.body.string() }
    }
}
